# deploy-component

The per-block half of `intropy deploy init`: one workload and one thin overlay
per environment.

Rendered once per topology component into
`domains/<domain>/<system>/<component>/`.

## Workload follows the block kind

An extractor wakes on a schedule, pulls from its source and exits, so it renders
a `CronJob`. Every other block reacts to messages or requests and must stay
resident, so it renders a `Deployment`. `spec.files` picks one and never writes
the other.

This is not an invention: the hand-written manifests in two customer
repositories already run their extractors as CronJobs. The topology's block kind
is what lets the CLI derive it.

The CronJob's schedule is a `REPLACE-ME-CRON-SCHEDULE` placeholder — and its
one and only home. Activation cadence is deployment configuration, so the
topology carries no schedule to copy from; fill it in here and edit it here.

## The image is never pinned here

`spec.values.imageTag` is the literal `unpinned`. `intropy deploy` writes the
digest, and it is the only thing that may. A tag here would let what runs change
without a deployment — which is what got an earlier version of this command
deleted.

`unpinned` rather than `latest` on purpose: it fails to pull loudly, and it reads
correctly in an `ImagePullBackOff`.

## imageNamespace has no safe default

Customers disagree on the path segment between the registry and the image name —
one uses the customer slug, another uses `integrations`. The default is
`integrations`; override it with `--set imageNamespace=<slug>` or a values file.
It must match what CI actually pushes.

## Reserved values

`.env`, `.topology`, `.gitops`, `.component` (this block's derived record) and
`.scaffold` (its `.intropy/scaffold.json` values, or `{}`).
