import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const evalRoot = resolve(dirname(fileURLToPath(import.meta.url)));
const repositoryRoot = dirname(evalRoot);
const protocol = readJson(
  join(evalRoot, "protocol", "evidence-first-skill.json"),
);
const pilotProtocol = readJson(join(evalRoot, "protocol", "pilot.json"));
const tasks = readJson(join(evalRoot, "tasks", "pilot.json")).tasks;
const taskIds = tasks.map(({ id }) => id);

assert.equal(protocol.status, "frozen_before_model_execution");
assert.deepEqual(protocol.design.arms, ["native", "evidence_first_skill"]);
assert.deepEqual(protocol.design.taskIds, taskIds);
assert.equal(protocol.design.repetitions, 3);
assert.equal(protocol.design.totalRuns, 72);
assert.equal(
  protocol.design.totalRuns,
  protocol.design.taskIds.length
    * protocol.design.arms.length
    * protocol.design.repetitions,
);
assert.deepEqual(protocol.design.stageOne, {
  repetitions: 1,
  runs: 24,
  reviewRequiredBeforeRemainingRuns: true,
});
assert.equal(protocol.design.runNumberOffset, 2000);
assert.ok(protocol.design.promptTreatment.includes("identical task prompt"));
assert.ok(protocol.design.promptTreatment.includes("no explicit skill name"));

assert.equal(protocol.target.package, pilotProtocol.target.package);
assert.equal(protocol.target.version, pilotProtocol.target.version);
assert.equal(protocol.target.npmIntegrity, pilotProtocol.target.npmIntegrity);
assert.equal(protocol.agent.model, pilotProtocol.agent.model);
assert.equal(
  protocol.agent.reasoningEffort,
  pilotProtocol.agent.reasoningEffort,
);
assert.equal(protocol.agent.version, pilotProtocol.agent.version);
assert.equal(protocol.controls.scorerVersion, "1.1.0");
assert.equal(protocol.controls.sandbox, "read-only");
assert.equal(protocol.controls.repositoryToolNetworkAccess, false);
assert.equal(protocol.controls.maxConsecutiveSessionFailures, 2);
assert.ok(protocol.controls.authentication.includes("temporary user-private symlink"));
assert.deepEqual(protocol.amendments[0].afterRunIds, [
  "SJ-RUN-2006-0FBDF4F7",
  "SJ-RUN-2007-18835DFA",
]);
assert.ok(protocol.amendments[0].retention.includes("remain unchanged"));
assert.equal(protocol.authorization.contractMergeDoesNotAuthorizeModelCalls, true);
assert.equal(protocol.authorization.stageOneRequiresSeparateOwnerApproval, true);
assert.equal(
  protocol.authorization.remainingRunsRequireSeparateOwnerApproval,
  true,
);
assert.equal(
  protocol.authorization.remainingRunsApproval.cumulativeTokenCeiling,
  7000000,
);
assert.ok(
  protocol.authorization.remainingRunsApproval.externalPurchaseCeiling
    .includes("No purchase"),
);

assert.ok(tasks.some(({ indexState }) => indexState === "warm"));
assert.ok(tasks.some(({ indexState }) => indexState === "none"));
assert.ok(protocol.gates.selectiveRouting.includes("native-only"));
assert.ok(protocol.gates.selectiveRouting.includes("Sanjaya-using"));
assert.ok(protocol.gates.sideEffects.includes("zero index writes"));

const pluginRoot = join(repositoryRoot, "plugins", "sanjaya");
assert.equal(
  sha256(join(pluginRoot, ".codex-plugin", "plugin.json")),
  protocol.target.plugin.manifestSha256,
);
assert.equal(
  sha256(
    join(
      pluginRoot,
      "skills",
      "evidence-first-code-discovery",
      "SKILL.md",
    ),
  ),
  protocol.target.plugin.skillSha256,
);

console.log(
  "Verified the frozen 72-run evidence-first skill study, including its "
  + "fresh control, 24-run review gate, exact plugin hashes, scorer 1.1, "
  + "selective-routing criterion, side-effect guard, and separate model-call "
  + "authorization boundary.",
);

function sha256(path) {
  return createHash("sha256").update(readFileSync(path)).digest("hex");
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}
