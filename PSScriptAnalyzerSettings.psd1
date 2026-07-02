@{
    # PSScriptAnalyzer Settings for Launcher Project
    # These settings suppress warnings for legitimate use cases in build/update scripts

    Rules = @{
        PSAvoidUsingWriteHost = @{
            Enable = $false
        }
        PSAvoidUsingInvokeExpression = @{
            Enable = $false
        }
        PSUseCatchBlockForTypeConversion = @{
            Enable = $false
        }
        PSReviewUnusedParameter = @{
            Enable = $false
        }
        PSUseUsingScopeModifierInNewRunspaces = @{
            Enable = $false
        }
    }
}
