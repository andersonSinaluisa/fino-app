/**
 * Split out from shareImport.ts itself: with tsconfig.json's `moduleSuffixes`
 * active, tsc resolves a bare `./shareImport` import to `shareImport.ios.ts`
 * project-wide (the same rule that lets Metro pick the right platform file
 * at bundle time forces a single choice for type-checking too) -- so a type
 * re-exported from inside shareImport.ios.ts itself would vanish from every
 * other file's point of view. Living in its own suffix-free file sidesteps
 * that entirely.
 */
export interface PendingSharedFile {
  uri: string;
  name: string;
}
