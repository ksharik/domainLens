param(
    [string] $MarkerPath = "__DOMAINLENS_EXTERNAL_MARKER__"
)

# This script is intentionally inert test data. DomainLens must never launch it.
[System.IO.File]::WriteAllText($MarkerPath, "REPOSITORY_CONTROLLED_SCRIPT_EXECUTED")
