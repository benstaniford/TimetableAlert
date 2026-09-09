#!/usr/bin/env python3
"""Regenerate a TimetableAlert timetable from the school's live Canvas calendar.

Lesson times come from the Canvas calendar feed — an .ics URL that carries its own
token and so needs no password. Teacher names are not in that feed, so they are held
in the config file and refreshed with --refresh-teachers, which is the only part that
logs in (with credentials taken from the environment, never from a file).

    ./scripts/fetch-timetable.py                     # this week -> timetable.json
    ./scripts/fetch-timetable.py --week next
    CANVAS_USER=... CANVAS_PASS=... ./scripts/fetch-timetable.py --refresh-teachers

See scripts/timetable-source.example.json for the config, and README.md for how to
find your own feed URL.
"""

import argparse
import http.cookiejar
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import date, datetime, timedelta, timezone

try:
    from zoneinfo import ZoneInfo
except ImportError:  # pragma: no cover - Python < 3.9
    sys.exit("This script needs Python 3.9 or newer.")

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_CONFIG = os.path.join(REPO, "scripts", "timetable-source.json")
DEFAULT_OUTPUT = os.path.join(REPO, "timetable.json")
DAYS = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]

# Course names that exist to administer a year group rather than to teach a subject.
# Every teacher in the school is enrolled on them, so they can never name a lesson's
# teacher; pin those in the config's "courses" map by hand instead.
ADMIN_TEACHER = re.compile(r"\bOperations\b|^Test Student", re.I)


# --------------------------------------------------------------------------- config


def load_config(path):
    if not os.path.exists(path):
        sys.exit(
            f"No config at {path}.\n"
            f"Copy scripts/timetable-source.example.json to that name and put your "
            f"calendar feed URL in it (README: 'Regenerating the timetable')."
        )
    with open(path, encoding="utf-8") as handle:
        config = json.load(handle)
    if not config.get("feedUrl"):
        sys.exit(f"{path} has no 'feedUrl'.")
    return config


def save_config(path, config):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(config, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


# ------------------------------------------------------------------------------ ics


def unfold(text):
    """Undo RFC 5545 line folding: a continuation line starts with a space."""
    return text.replace("\r\n", "\n").replace("\n ", "").replace("\n\t", "")


def unescape(value):
    return (
        value.replace("\\n", "\n").replace("\\,", ",").replace("\\;", ";").replace("\\\\", "\\")
    )


def parse_ics(text):
    """Yield the timed events in an iCalendar document.

    All-day entries (assignment due dates, in a Canvas feed) have a DTSTART of
    VALUE=DATE and are skipped: they are not lessons and have no start time.
    """
    events = []
    for block in unfold(text).split("BEGIN:VEVENT")[1:]:
        block = block.split("END:VEVENT")[0]
        fields = {}
        for line in block.split("\n"):
            if ":" not in line:
                continue
            name, value = line.split(":", 1)
            fields.setdefault(name.split(";")[0], value)
        start, end = fields.get("DTSTART"), fields.get("DTEND")
        if not start or not re.fullmatch(r"\d{8}T\d{6}Z", start):
            continue
        events.append(
            {
                "start": datetime.strptime(start, "%Y%m%dT%H%M%SZ").replace(tzinfo=timezone.utc),
                "end": (
                    datetime.strptime(end, "%Y%m%dT%H%M%SZ").replace(tzinfo=timezone.utc)
                    if end and re.fullmatch(r"\d{8}T\d{6}Z", end)
                    else None
                ),
                "summary": unescape(fields.get("SUMMARY", "")),
                "course": course_of(fields.get("URL", "")),
            }
        )
    return events


def course_of(url):
    """Canvas puts the owning course in the event's URL as include_contexts=course_123."""
    match = re.search(r"course_(\d+)", url or "")
    return match.group(1) if match else None


# -------------------------------------------------------------------------- tidying


def subject_of(summary, config):
    """Turn a calendar entry's title into the subject name to put on the banner.

    'Y10 Maths Higher Core Luna [10MaHLun]' -> 'Maths Higher'. The trailing code is
    always dropped; then an exact match in the config's "subjects" map wins, and
    failing that the year-group prefix and any trailing "dropWords" (teaching set
    names and filler like 'Core') come off.
    """
    title = re.sub(r"\s*\[[^\]]*\]\s*$", "", summary).strip()
    overrides = config.get("subjects", {})
    if title in overrides:
        return overrides[title]
    title = re.sub(r"^(Y\d+|Year \d+|Whole School)\s+", "", title).strip()
    drop = {word.lower() for word in config.get("dropWords", [])}
    words = title.split()
    while len(words) > 1 and words[-1].lower() in drop:
        words.pop()
    return " ".join(words)


def teacher_of(course_id, config):
    entry = config.get("courses", {}).get(str(course_id), {})
    return entry.get("teacher") or None


# ----------------------------------------------------------------------------- week


def week_start(spec, tz):
    """Monday of the requested week: 'this', 'next', 'last' or an ISO date in it."""
    today = datetime.now(tz).date()
    if spec in ("this", "next", "last"):
        monday = today - timedelta(days=today.weekday())
        return monday + timedelta(weeks={"this": 0, "next": 1, "last": -1}[spec])
    try:
        chosen = date.fromisoformat(spec)
    except ValueError:
        sys.exit(f"--week wants 'this', 'next', 'last' or a YYYY-MM-DD date, not {spec!r}.")
    return chosen - timedelta(days=chosen.weekday())


def build_timetable(events, config, monday, tz):
    end_of_week = monday + timedelta(days=7)
    lessons = []
    for event in events:
        local = event["start"].astimezone(tz)
        if not monday <= local.date() < end_of_week:
            continue
        lesson = {
            "day": DAYS[local.weekday()],
            "start": local.strftime("%H:%M"),
            "subject": subject_of(event["summary"], config),
            "teacher": teacher_of(event["course"], config),
        }
        if event["end"]:
            lesson["end"] = event["end"].astimezone(tz).strftime("%H:%M")
        lessons.append((local, lesson))

    lessons.sort(key=lambda pair: pair[0])
    timetable = {}
    if config.get("student"):
        timetable["student"] = config["student"]
    if config.get("alerts"):
        timetable["alerts"] = config["alerts"]
    # Key order matches timetable.sample.json so the two files diff sensibly.
    timetable["lessons"] = [
        {key: lesson[key] for key in ("day", "start", "end", "subject", "teacher") if key in lesson}
        for _, lesson in lessons
    ]
    return timetable


# ------------------------------------------------------- teacher refresh (needs login)


def canvas_login(base, user, password):
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    opener.addheaders = [("User-Agent", "TimetableAlert/fetch-timetable")]

    with opener.open(f"{base}/login/canvas", timeout=30) as response:
        page = response.read().decode("utf-8", "replace")
    match = re.search(r'name="authenticity_token"\s+value="([^"]+)"', page)
    if not match:
        sys.exit("Could not find the login form's authenticity token — has Canvas changed?")

    form = urllib.parse.urlencode(
        {
            "authenticity_token": match.group(1),
            "pseudonym_session[unique_id]": user,
            "pseudonym_session[password]": password,
            "pseudonym_session[remember_me]": "0",
        }
    ).encode()
    try:
        with opener.open(f"{base}/login/canvas", data=form, timeout=30) as response:
            # A good login redirects to /?login_success=1; a bad one re-renders the form.
            if "login_success" not in response.geturl():
                sys.exit("Canvas rejected those credentials.")
    except urllib.error.HTTPError as error:
        # Canvas answers a failed login with 400 rather than redisplaying the form.
        if error.code == 400:
            sys.exit("Canvas rejected those credentials.")
        raise
    return opener


def api(opener, base, path):
    with opener.open(f"{base}{path}", timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def refresh_teachers(config, config_path):
    user, password = os.environ.get("CANVAS_USER"), os.environ.get("CANVAS_PASS")
    if not user or not password:
        sys.exit(
            "--refresh-teachers needs CANVAS_USER and CANVAS_PASS in the environment.\n"
            "They are used for this one request and are never written to disk."
        )
    base = "{0.scheme}://{0.netloc}".format(urllib.parse.urlsplit(config["feedUrl"]))
    opener = canvas_login(base, user, password)

    courses = config.setdefault("courses", {})
    for course in api(opener, base, "/api/v1/courses?per_page=100"):
        entry = courses.setdefault(str(course["id"]), {})
        entry["name"] = course["name"]
        staff = api(
            opener, base, f"/api/v1/courses/{course['id']}/users?enrollment_type[]=teacher&per_page=50"
        )
        named = [person["name"] for person in staff if not ADMIN_TEACHER.search(person["name"])]
        # Only a single teacher identifies the lesson. Year-group and whole-school
        # courses list dozens, so leave whatever the config already says for those.
        if len(named) == 1:
            entry["teacher"] = named[0]
        else:
            entry.setdefault("teacher", None)
        print(f"  {course['name']}: {entry['teacher'] or '(set by hand)'}", file=sys.stderr)

    save_config(config_path, config)
    print(f"Teachers written to {config_path}", file=sys.stderr)


# ----------------------------------------------------------------------------- main


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--config", default=DEFAULT_CONFIG, help="config file (default: %(default)s)")
    parser.add_argument("--week", default="this", help="this, next, last or a YYYY-MM-DD date")
    parser.add_argument("-o", "--output", default=DEFAULT_OUTPUT, help="default: %(default)s")
    parser.add_argument(
        "--refresh-teachers",
        action="store_true",
        help="log in and rewrite the teacher names in the config, then carry on",
    )
    args = parser.parse_args()

    config = load_config(args.config)
    if args.refresh_teachers:
        refresh_teachers(config, args.config)

    tz = ZoneInfo(config.get("timeZone", "Europe/London"))
    monday = week_start(args.week, tz)

    request = urllib.request.Request(
        config["feedUrl"], headers={"User-Agent": "TimetableAlert/fetch-timetable"}
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        feed = response.read().decode("utf-8", "replace")

    timetable = build_timetable(parse_ics(feed), config, monday, tz)
    if not timetable["lessons"]:
        sys.exit(f"No lessons found in the week beginning {monday} — a holiday, or the wrong week?")

    with open(args.output, "w", encoding="utf-8") as handle:
        json.dump(timetable, handle, indent=2, ensure_ascii=False)
        handle.write("\n")

    print(
        f"{len(timetable['lessons'])} lessons for the week beginning {monday} -> {args.output}",
        file=sys.stderr,
    )


if __name__ == "__main__":
    main()
