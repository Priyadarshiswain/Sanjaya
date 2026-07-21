# TypeScript compiler distribution gate

No TypeScript compiler files are vendored in the Sanjaya scaffold.

Before a compiler runtime is added, the project must record and review:

1. The exact official package version and source artifact.
2. A SHA-256 checksum of the distributed artifact.
3. The matching upstream `LICENSE.txt`.
4. The matching upstream `ThirdPartyNoticeText.txt`.
5. An entry in Sanjaya's `NOTICE` and `THIRD-PARTY-NOTICES.txt` files.
6. A test proving that the npm package contains the required notices.

The runtime must be obtained from an official TypeScript distribution. Files
from an editor installation or another local application are not acceptable
release provenance.
