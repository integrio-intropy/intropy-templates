# Samples

Put real sample files here: an input as the source delivers it and, next to it,
the output the destination expects for it. Both test projects copy this folder
to their output as `samples/`, so a test reads one with
`File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "samples", "<name>"))`.

Replace personal data with mock values before committing a sample.
