// The Burst linker writes the current time into the unsigned Windows DLL's
// COFF header. Normalize that metadata as part of every Release build.
// This does not change sections, executable instructions or runtime behaviour.
const fs = require('node:fs');
const file = process.argv[2];
const bytes = fs.readFileSync(file);
if (bytes.length < 256 || bytes.toString('ascii', 0, 2) !== 'MZ') throw Error('Expected a Windows PE DLL');
const pe = bytes.readUInt32LE(0x3c);
if (pe + 264 > bytes.length || bytes.readUInt32LE(pe) !== 0x4550 || bytes.readUInt16LE(pe + 4) !== 0x8664 || bytes.readUInt16LE(pe + 24) !== 0x20b)
  throw Error('Expected an x64 PE32+ DLL');
if (bytes.readUInt32LE(pe + 24 + 144) !== 0 || bytes.readUInt32LE(pe + 24 + 148) !== 0)
  throw Error('Refusing to normalize a signed DLL');
if (bytes.readUInt32LE(pe + 24 + 64) !== 0) throw Error('Unexpected nonzero PE checksum');
bytes.writeUInt32LE(0, pe + 8);
fs.writeFileSync(file, bytes);
console.log('Normalized native DLL build timestamp.');
