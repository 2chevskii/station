$env:STARSHIP_CONFIG = "${starship_config_path}"
Invoke-Expression (& starship init powershell)

function prompt {
    " ${shell_prompt_symbol} "
}
