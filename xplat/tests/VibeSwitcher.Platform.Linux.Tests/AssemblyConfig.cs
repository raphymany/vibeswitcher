// Some tests set process-wide environment variables or start real processes; run classes one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
