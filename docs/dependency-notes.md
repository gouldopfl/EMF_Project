
## Paddle oneDNN API deprecation

Sdcb.PaddleInference 3.3.1 currently emits a Paddle 3.x warning that the
legacy MKLDNN cache-capacity API is deprecated in favor of
PD_ConfigSetOnednnCacheCapacity.

EMF does not call this native API directly. Current Sdcb Paddle packages
are up to date, so this remains an upstream dependency issue.

Recheck when upgrading Sdcb.PaddleOCR or Sdcb.PaddleInference runtimes.
