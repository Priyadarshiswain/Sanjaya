import { readFileSync, readdirSync, writeFileSync } from "node:fs";
import { join, resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const claims = new Set([
  "SJ-EVAL-0007:hit_guard",
  "SJ-EVAL-0007:produce_failure",
  "SJ-EVAL-0008:overlapped_work",
  "SJ-EVAL-0008:failure_policy",
  "SJ-EVAL-0010:child_cleanup",
]);
const rows = [];
for (const study of ["pilot", "guided", "evidence-first-skill"]) {
  const runsRoot = join(evalRoot, "results", "v0.1.2", study, "runs");
  for (const file of readdirSync(runsRoot).filter((f) => f.endsWith(".json")).sort()) {
    const run = JSON.parse(readFileSync(join(runsRoot, file), "utf8"));
    if (run.status !== "completed" || !run.answer) continue;
    for (const claim of run.answer.claims) {
      if (claims.has(`${run.taskId}:${claim.key}`)) {
        rows.push({ taskId: run.taskId, key: claim.key, value: claim.value });
      }
    }
  }
}
rows.sort((a, b) =>
  a.taskId.localeCompare(b.taskId)
  || a.key.localeCompare(b.key)
  || a.value.localeCompare(b.value));
writeFileSync(
  join(evalRoot, "fixtures", "scorer-v1.2-review.json"),
  `${JSON.stringify(rows, null, 2)}\n`,
);
console.log(`Wrote ${rows.length} arm-blind supplied values.`);