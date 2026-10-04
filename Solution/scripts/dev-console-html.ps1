Set-StrictMode -Version Latest

function ConvertTo-DevConsoleHtmlText {
    param([AllowNull()][object]$Value)

    [System.Net.WebUtility]::HtmlEncode([string]$Value)
}
