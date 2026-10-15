# Turns failed tests from the .trx result files into GitHub annotations and a job summary, so the failures can be
# read on the pull request without opening the full log.
import glob
import os
import xml.etree.ElementTree as ET

ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
lines = ["### Failed tests"]
for f in glob.glob("**/TestResults/*.trx", recursive=True):
    for r in ET.parse(f).getroot().iterfind(".//t:UnitTestResult", ns):
        if r.get("outcome") != "Failed":
            continue
        msg = r.findtext(".//t:ErrorInfo/t:Message", default="", namespaces=ns).strip()
        stack = r.findtext(".//t:ErrorInfo/t:StackTrace", default="", namespaces=ns).strip().splitlines()
        text = (r.get("testName") + " :: " + msg + " @ " + (stack[0].strip() if stack else "")).replace("\r", " ").replace("\n", " | ")
        print("::error title=Failed test::" + text[:1500])
        lines.append("- " + r.get("testName"))
with open(os.environ["GITHUB_STEP_SUMMARY"], "a") as summary:
    summary.write("\n".join(lines) + "\n")
