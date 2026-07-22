export const releaseVersion = "0.1.1";
export const publishedVersion = "0.1.0";
export const releaseTag = `v${releaseVersion}`;
export const packageName = "sanjaya-mcp";
export const registryName = "io.github.Priyadarshiswain/sanjaya";

// Candidate means the source and artifact contract is reviewed, not published.
// Change this only after npm publication is independently verified.
export const publicationState = "candidate";

export const releaseArtifactDirectory = "dist/release";
export const releaseTarballName = `${packageName}-${releaseVersion}.tgz`;

export function assertReleasePackage(packageDocument) {
  if (packageDocument.name !== packageName || packageDocument.version !== releaseVersion) {
    throw new Error(`Release metadata must identify ${packageName}@${releaseVersion}.`);
  }
  if (Object.hasOwn(packageDocument, "private")) {
    throw new Error("The release candidate package must not contain npm's private publication lock.");
  }
  const expectedPublishConfig = { access: "public", provenance: true };
  if (JSON.stringify(packageDocument.publishConfig) !== JSON.stringify(expectedPublishConfig)) {
    throw new Error("The release candidate must require public access and npm provenance.");
  }
}
