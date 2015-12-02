﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Composition;
using System.Linq;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis.BinaryParsers.PortableExecutable;
using Microsoft.CodeAnalysis.BinaryParsers.ProgramDatabase;
using Microsoft.CodeAnalysis.IL.Sdk;

namespace Microsoft.CodeAnalysis.IL.Rules
{
    [Export(typeof(IBinarySkimmer))]
    public class InitializeStackProtection : IBinarySkimmer, IRuleContext
    {
        public string Id { get { return RuleConstants.InitializeStackProtectionId; } }

        public string Name { get { return nameof(InitializeStackProtection); } }

        public void Initialize(BinaryAnalyzerContext context) { return; }

        public AnalysisApplicability CanAnalyze(BinaryAnalyzerContext context, out string reasonForNotAnalyzing)
        {
            return StackProtectionUtilities.CommonCanAnalyze(context, out reasonForNotAnalyzing);
        }

        public void Analyze(BinaryAnalyzerContext context)
        {
            PEHeader peHeader = context.PE.PEHeaders.PEHeader;

            if (context.Pdb == null)
            {
                context.Logger.Log(MessageKind.Fail, context,
                    RuleUtilities.BuildCouldNotLoadPdbMessage(context));
                return;
            }

            Pdb di = context.Pdb;

            bool noCode = !di.CreateGlobalFunctionIterator().Any() && !di.ContainsExecutableSectionContribs();

            if (noCode)
            {
                // '{0}' is a C or C++ binary that is not required to initialize the stack protection, as it does not contain executable code.
                context.Logger.Log(MessageKind.Pass, context,
                    RuleUtilities.BuildMessage(context,
                        RulesResources.InitializeStackProtection_NoCode_Pass));
                return;
            }

            bool bHasGSCheck = di.CreateGlobalFunctionIterator(
                StackProtectionUtilities.GSCheckFunctionName, NameSearchOptions.nsfCaseSensitive).Any();

            bool bHasGSInit = StackProtectionUtilities.GSInitializationFunctionNames.Any(
                                functionName => di.CreateGlobalFunctionIterator(functionName,
                                                                                NameSearchOptions.nsfCaseSensitive).Any());

            if (!bHasGSCheck && !bHasGSInit)
            {
                // '{0}' is a C or C++ binary that does not make use of the stack protection 
                // buffer security feature. It is therefore not required to initialize the feature.
                context.Logger.Log(MessageKind.Pass, context,
                    RuleUtilities.BuildMessage(context,
                        RulesResources.InitializeStackProtection_NoFeatureUse_Pass));
                return;
            }

            if (!bHasGSInit)
            {
                // '{0}' is a C or C++ binary that does not initialize the stack protector. 
                // The stack protector(/ GS) is a security feature of the compiler which 
                // makes it more difficult to exploit stack buffer overflow memory 
                // corruption vulnerabilities. The stack protector requires access to 
                // entropy in order to be effective, which means a binary must initialize 
                // a random number generator at startup, by calling __security_init_cookie() 
                // as close to the binary's entry point as possible. Failing to do so will 
                // result in spurious buffer overflow detections on the part of the stack 
                // protector. To resolve this issue, use the default entry point provided 
                // by the C runtime, which will make this call for you, or call 
                // __security_init_cookie() manually in your custom entry point.
                context.Logger.Log(MessageKind.Fail, context,
                    RuleUtilities.BuildMessage(context,
                        RulesResources.InitializeStackProtection_Fail));
                return;
            }

            // '{0}' is a C or C++ binary built with the buffer security feature 
            // that properly initializes the stack protecter. This has the 
            //effect of increasing the effectiveness of the feature and reducing 
            // spurious detections.
            context.Logger.Log(MessageKind.Pass, context,
                RuleUtilities.BuildMessage(context,
                   RulesResources.InitializeStackProtection_Pass));
        }
    }
}
