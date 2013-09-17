﻿using System;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace NameOf.Fody {
    public class ModuleWeaver {
        public ModuleDefinition ModuleDefinition { get; set; }
        public void Execute() {
            foreach (var typeDefinition in ModuleDefinition.GetTypes()) {
                foreach (var methodDefinition in typeDefinition.Methods.Where(x => x.HasBody))
                    ProcessMethod(methodDefinition);
            }
            // TODO: Remove Name.Of assembly and all references to it
        }
        private static readonly MethodInfo NameOfMethodInfo = new Func<Object, String>(Name.Of).Method;
        private static readonly String NameOfMethodSignature = String.Format("{0} {1}::{2}", NameOfMethodInfo.ReturnType, NameOfMethodInfo.DeclaringType, NameOfMethodInfo.Name);
        private static Boolean IsNameOfCallInstruction(Instruction instruction) {
            return instruction.OpCode == OpCodes.Call && ((MethodReference) instruction.Operand).FullName.StartsWith(NameOfMethodSignature);
        }
        private static void ProcessMethod(MethodDefinition methodDefinition) {
            var nameOfCallInstructions = methodDefinition.Body.Instructions.Where(IsNameOfCallInstruction).ToList();
            var methodBodyIlProcessor = methodDefinition.Body.GetILProcessor();
            foreach (var nameOfCallInstruction in nameOfCallInstructions)
                ProcessNameOfCallInstruction(nameOfCallInstruction, methodBodyIlProcessor);
        }
        private delegate Boolean NameOfCallInstructionProcessor(Instruction instruction, ILProcessor ilProcessor);
        private static void ProcessNameOfCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            var processNameOfCallInstructionDelegates = new NameOfCallInstructionProcessor[] {
                                                                                                 ProcessStaticNameOfEventCallInstruction,
                                                                                                 ProcessInstanceNameOfEventCallInstruction,
                                                                                                 ProcessStaticNameOfFieldCallInstruction,
                                                                                                 ProcessNameOfTypeCallInstruction,
                                                                                                 ProcessNameOfArgumentCallInstruction,
                                                                                                 ProcessNameOfLocalCallInstruction,
                                                                                                 ProcessNameOfFieldCallInstruction,
                                                                                                 ProcessNameOfPropertyCallInstruction,
                                                                                                 ProcessStaticNameOfStaticMethodCallInstruction,
                                                                                             };
            Exception innerException;
            try {
                foreach (var processNameOfCallInstructionDelegate in processNameOfCallInstructionDelegates) {
                    if (processNameOfCallInstructionDelegate(instruction, ilProcessor))
                        return;
                }
                return; // TODO: Remove this so the exception works!
            }
            catch (NotSupportedException notSupportedException) {
                innerException = notSupportedException;
            }
            // The usage of Name.Of is not supported
            var i = instruction;
            while (i.SequencePoint == null) // Look for last sequence point
                i = i.Previous;
            String exceptionMessage = String.Format("This usage of '{0}.{1}' is not supported. Source: {2} - line {3}",
                NameOfMethodInfo.DeclaringType,
                NameOfMethodInfo.Name,
                i.SequencePoint.Document.Url,
                i.SequencePoint.StartLine);
            throw new NotSupportedException(exceptionMessage, innerException);
        }
        
        private static readonly OpCode[] CallOpCodes = {OpCodes.Call, OpCodes.Calli, OpCodes.Callvirt};
        private static readonly MethodInfo GetTypeFromHandleMethodInfo = new Func<RuntimeTypeHandle, Object>(Type.GetTypeFromHandle).Method;
        private static readonly String GetTypeFromHandleMethodSignature = String.Format("{0} {1}::{2}(", GetTypeFromHandleMethodInfo.ReturnType, GetTypeFromHandleMethodInfo.DeclaringType, GetTypeFromHandleMethodInfo.Name);
        private static Boolean ProcessNameOfTypeCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            var genericInstanceMethod = instruction.Operand as GenericInstanceMethod;
            if (genericInstanceMethod == null) {
                var previous = instruction.Previous;
                if (previous != null && previous.OpCode == OpCodes.Call && previous.Operand is MethodReference && ((MethodReference) previous.Operand).FullName.StartsWith(GetTypeFromHandleMethodSignature)) {
                    if (previous.Previous != null && previous.Previous.OpCode == OpCodes.Ldtoken) {
                        String name = ((TypeDefinition) previous.Previous.Operand).Name;
                        ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
                        ilProcessor.Remove(instruction.Previous.Previous);
                        ilProcessor.Remove(instruction.Previous);
                        ilProcessor.Remove(instruction);
                        return true;
                    }
                }
            }
            else if (genericInstanceMethod.Parameters.Count == 0 && genericInstanceMethod.GenericArguments.Count == 1) {
                String name = genericInstanceMethod.GenericArguments[0].Name;
                ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
                ilProcessor.Remove(instruction);
                return true;
            }
            return false;
        }
        private static Boolean ProcessNameOfArgumentCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (instruction.Previous == null)
                return false;
            var isBoxed = instruction.Previous.OpCode == OpCodes.Box;
            var potentialLoadLocalInstruction = isBoxed ? instruction.Previous.Previous : instruction.Previous;
            String name;
            if (new[] {OpCodes.Ldarg, OpCodes.Ldarg_S, OpCodes.Ldarga, OpCodes.Ldarga_S}.Contains(potentialLoadLocalInstruction.OpCode))
                name = ((ParameterReference) potentialLoadLocalInstruction.Operand).Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldarg_0)
                name = ilProcessor.Body.Method.Parameters[0].Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldarg_1)
                name = ilProcessor.Body.Method.Parameters[1].Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldarg_2)
                name = ilProcessor.Body.Method.Parameters[2].Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldarg_3)
                name = ilProcessor.Body.Method.Parameters[3].Name;
            else
                return false;
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            if (isBoxed)
                ilProcessor.Remove(instruction.Previous.Previous);
            ilProcessor.Remove(instruction.Previous);
            ilProcessor.Remove(instruction);
            return true;
        }
        private static Boolean ProcessNameOfLocalCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (instruction.Previous == null)
                return false;
            var isBoxed = instruction.Previous.OpCode == OpCodes.Box;
            var potentialLoadLocalInstruction = isBoxed ? instruction.Previous.Previous : instruction.Previous;
            String name;
            if (new[] {OpCodes.Ldloc, OpCodes.Ldloc_S, OpCodes.Ldloca, OpCodes.Ldloca_S}.Contains(potentialLoadLocalInstruction.OpCode))
                name = ((VariableReference) potentialLoadLocalInstruction.Operand).Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldloc_0)
                name = ilProcessor.Body.Variables[0].Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldloc_1)
                name = ilProcessor.Body.Variables[1].Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldloc_2)
                name = ilProcessor.Body.Variables[2].Name;
            else if (potentialLoadLocalInstruction.OpCode == OpCodes.Ldloc_3)
                name = ilProcessor.Body.Variables[3].Name;
            else
                return false;
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            if (isBoxed)
                ilProcessor.Remove(instruction.Previous.Previous);
            ilProcessor.Remove(instruction.Previous);
            ilProcessor.Remove(instruction);
            return true;
        }
        private static Boolean ProcessNameOfFieldCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (instruction.Previous == null)
                return false;
            var isBoxed = instruction.Previous.OpCode == OpCodes.Box;
            var potentialLoadLocalInstruction = isBoxed ? instruction.Previous.Previous : instruction.Previous;
            if (!new[] {OpCodes.Ldfld, OpCodes.Ldsfld}.Contains(potentialLoadLocalInstruction.OpCode))
                return false;
            //Boolean isALambdaExpression = ((FieldReference)potentialLoadLocalInstruction.Operand).FieldType.FullName == typeof(Action).FullName;
            //if (isALambdaExpression)
            //    throw new NotSupportedException("Lamdbda expressions are not supported.");
            String name = ((FieldReference) potentialLoadLocalInstruction.Operand).Name;
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            if (isBoxed)
                ilProcessor.Remove(instruction.Previous.Previous);
            ilProcessor.Remove(instruction.Previous);
            ilProcessor.Remove(instruction);
            return true;
        }
        private static Boolean ProcessNameOfPropertyCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (instruction.Previous == null)
                return false;
            var isBoxed = instruction.Previous.OpCode == OpCodes.Box;
            var potentialLoadLocalInstruction = isBoxed ? instruction.Previous.Previous : instruction.Previous;
            if (!CallOpCodes.Contains(potentialLoadLocalInstruction.OpCode))
                return false;
            var methodReference = (MethodDefinition) potentialLoadLocalInstruction.Operand;
            if (!methodReference.IsGetter)
                return false;
            String name = methodReference.Name.Substring(4); // remove leading "get_"
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            if (isBoxed)
                ilProcessor.Remove(instruction.Previous.Previous);
            ilProcessor.Remove(instruction.Previous);
            ilProcessor.Remove(instruction);
            return true;
        }
        private static Boolean ProcessStaticNameOfStaticMethodCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (instruction.Previous == null || instruction.Previous.Previous == null || instruction.Previous.Previous.Previous == null)
                return false;
            if (instruction.Previous.OpCode != OpCodes.Newobj || instruction.Previous.Previous.OpCode != OpCodes.Ldftn || instruction.Previous.Previous.Previous.OpCode != OpCodes.Ldnull)
                return false;
            var methodDefinition = (MethodReference) instruction.Previous.Previous.Operand;
            String name = methodDefinition.Name;
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            ilProcessor.Remove(instruction.Previous.Previous.Previous);
            ilProcessor.Remove(instruction.Previous.Previous);
            ilProcessor.Remove(instruction.Previous);
            ilProcessor.Remove(instruction);
            return true;
        }
        private static readonly OpCode[] StaticNameOfLambdaCallInstructionOpCodePattern = {
                                                                                              OpCodes.Ldsfld,
                                                                                              OpCodes.Brtrue_S,
                                                                                              OpCodes.Ldnull,
                                                                                              OpCodes.Ldftn,
                                                                                              OpCodes.Newobj,
                                                                                              OpCodes.Stsfld,
                                                                                              OpCodes.Br_S,
                                                                                              OpCodes.Ldsfld
                                                                                          };
        
        private static readonly MethodInfo NameOfEventMethodInfo = new Func<Action<Object, EventHandler>, String>(Name.OfEvent).Method;
        private static readonly String NameOfEventMethodSignature = String.Format("{0} {1}::{2}", NameOfEventMethodInfo.ReturnType, NameOfEventMethodInfo.DeclaringType, NameOfEventMethodInfo.Name);
        private static Boolean ProcessStaticNameOfEventCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (!((MethodReference) instruction.Operand).FullName.StartsWith(NameOfEventMethodSignature))
                return false;
            Instruction hold = instruction;
            for (var i = 0; i != StaticNameOfLambdaCallInstructionOpCodePattern.Count(); ++i) {
                if ((hold = hold.Previous) == null)
                    return false;
                if (hold.OpCode != StaticNameOfLambdaCallInstructionOpCodePattern.Reverse().ElementAt(i))
                    return false;
            }
            var anonymousMethod = (MethodDefinition) instruction.Previous.Previous.Previous.Previous.Previous.Operand;
            var loadEventFieldInstruction = ((MethodReference) anonymousMethod.Body.Instructions.Single(x => CallOpCodes.Contains(x.OpCode)).Operand);
            String name = loadEventFieldInstruction.Name;
            RemoveEventFieldPrefixes(ref name);
            // TODO: Remove anonymous method using ilProcessor.Body.Method.DeclaringType.Methods.
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            hold = instruction;
            for (var i = 0; i != StaticNameOfLambdaCallInstructionOpCodePattern.Count() + 1; ++i) {
                var temp = hold.Previous;
                ilProcessor.Remove(hold);
                hold = temp;
            }
            return true;
        }
        private static readonly OpCode[] LdlocOpCodes = {OpCodes.Ldloc, OpCodes.Ldloca, OpCodes.Ldloca_S, OpCodes.Ldloca_S, OpCodes.Ldloc_0, OpCodes.Ldloc_1, OpCodes.Ldloc_2, OpCodes.Ldloc_3};
        private static Boolean ProcessInstanceNameOfEventCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (!((MethodReference) instruction.Operand).FullName.StartsWith(NameOfEventMethodSignature))
                return false;
            if (instruction.Previous == null || instruction.Previous.Previous == null || instruction.Previous.Previous.Previous == null)
                return false;
            if (instruction.Previous.OpCode != OpCodes.Newobj || instruction.Previous.Previous.OpCode != OpCodes.Ldftn || !LdlocOpCodes.Contains(instruction.Previous.Previous.Previous.OpCode))
                return false;
            var anonymousMethod = (MethodDefinition) instruction.Previous.Previous.Operand;
            String name = ((MethodReference) anonymousMethod.Body.Instructions.Single(x => CallOpCodes.Contains(x.OpCode)).Operand).Name;
            RemoveEventFieldPrefixes(ref name);
            // TODO: Remove anonymous method using ilProcessor.Body.Method.DeclaringType.Methods.
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            ilProcessor.Remove(instruction.Previous.Previous.Previous);
            ilProcessor.Remove(instruction.Previous.Previous);
            ilProcessor.Remove(instruction.Previous);
            ilProcessor.Remove(instruction);
            return true;
        }
        private static void RemoveEventFieldPrefixes(ref String fieldName) {
            const String addPrefix = "add_";
            const String removePrefix = "remove_";
            if (fieldName.StartsWith(addPrefix))
                fieldName = fieldName.Substring(addPrefix.Length);
            else if (fieldName.StartsWith(removePrefix))
                fieldName = fieldName.Substring(removePrefix.Length);
            else
                throw new NotSupportedException();
        }
        private static readonly MethodInfo NameOfFieldMethodInfo = new Func<Func<Object, Object>, String>(Name.OfField).Method;
        private static readonly String NameOfFieldMethodSignature = String.Format("{0} {1}::{2}", NameOfFieldMethodInfo.ReturnType, NameOfFieldMethodInfo.DeclaringType, NameOfFieldMethodInfo.Name);
        private static readonly OpCode[] LdfldOpCodes = {OpCodes.Ldfld, OpCodes.Ldflda, OpCodes.Ldsfld, OpCodes.Ldsflda};
        private static Boolean ProcessStaticNameOfFieldCallInstruction(Instruction instruction, ILProcessor ilProcessor) {
            if (!((MethodReference) instruction.Operand).FullName.StartsWith(NameOfFieldMethodSignature))
                return false;
            Instruction hold = instruction;
            for (var i = 0; i != StaticNameOfLambdaCallInstructionOpCodePattern.Count(); ++i) {
                if ((hold = hold.Previous) == null)
                    return false;
                if (hold.OpCode != StaticNameOfLambdaCallInstructionOpCodePattern.Reverse().ElementAt(i))
                    return false;
            }
            var anonymousMethod = (MethodDefinition) instruction.Previous.Previous.Previous.Previous.Previous.Operand;
            var loadEventFieldInstruction = (FieldDefinition) anonymousMethod.Body.Instructions.Single(x => LdfldOpCodes.Contains(x.OpCode)).Operand;
            String name = loadEventFieldInstruction.Name;
            // TODO: Remove anonymous method using ilProcessor.Body.Method.DeclaringType.Methods.
            ilProcessor.InsertAfter(instruction, Instruction.Create(OpCodes.Ldstr, name));
            hold = instruction;
            for (var i = 0; i != StaticNameOfLambdaCallInstructionOpCodePattern.Count() + 1; ++i) {
                var temp = hold.Previous;
                ilProcessor.Remove(hold);
                hold = temp;
            }
            return true;
        }
        private static Boolean ProcessNameOfMethodCallInstruction(Instruction instruction, ILProcessor processor) {
            var instructionOpCodePattern = new[] {
                                                     OpCodes.Ldfld,
                                                     OpCodes.Ldftn,
                                                     OpCodes.Newobj
                                                 };
            throw new NotImplementedException();
        }
    }
}