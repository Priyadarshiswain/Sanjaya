import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import {
  createMarketplaceFixture,
  removeMarketplaceFixture,
} from "./plugin-marketplace-contract.mjs";

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const fixture = createMarketplaceFixture(repositoryRoot);
removeMarketplaceFixture(repositoryRoot, fixture);

console.log(
  "Generated, verified, and removed an exact local-only Sanjaya marketplace "
  + "without installing or publishing it.",
);
