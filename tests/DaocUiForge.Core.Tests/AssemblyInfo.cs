using Xunit;

// The interface language is process-wide by design
// (DaocUiForge.Core.Localization.Strings): one program, one language, and
// nothing has to be told which one. That makes StringsTests a test that changes
// global state, and xunit runs collections in parallel — so a test asserting on
// an English message could read it mid-switch and fail for a reason that has
// nothing to do with it.
//
// Serial rather than a lock around the language: the whole suite is 24 seconds,
// and a lock every test has to remember to take is a trap for the next one
// written.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
