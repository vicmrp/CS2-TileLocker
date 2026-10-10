# Reproduce CS2-TileLocker 1.1.1

Install the CS2 modding toolchain, .NET SDK 10.0.302, and Node.js/npm. The game references are resolved by the normal CS2 toolchain; do not copy private game DLLs into the source repository.

From the exact release commit, run `uvm build --package <empty-output-folder>`. The build runs `npm ci` from the committed lockfile, produces the Windows package, and normalizes only the unsigned native PE timestamp. The recorded manifest identifies the exact game assembly and SDK. All executable and UI output files are declared in `uvm.json`.

Independent verifiers should inspect the source, compare their rebuild with the public manifest, then use `uvm attest --manifest release.json --package <rebuilt-folder>` with their own registered GitHub signing identity. A matching build is not a security audit.
