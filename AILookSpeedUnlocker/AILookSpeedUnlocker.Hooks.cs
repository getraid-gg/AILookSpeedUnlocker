using AIProject;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace AILookSpeedUnlocker
{
    class Hooks
    {
        [HarmonyPatch(typeof(CustomAxisState), "Update", typeof(float))]
        internal static class CustomAxisStateUpdatePatch
        {
            private static IEnumerable<CodeInstruction> Transpiler(ILGenerator generator, IEnumerable<CodeInstruction> instructions)
            {
                var instructionList = new List<CodeInstruction>(instructions);
                var instructionEnumerator = instructions.GetEnumerator();
                
                // Part 1: remove the clamp on axes
                while (instructionEnumerator.MoveNext())
                {
                    var instruction = instructionEnumerator.Current;
                    if (instruction.opcode == OpCodes.Ldc_R4 && (float)instruction.operand == -1.0f)
                    {
                        // This instruction and the next two push -1 and +1 to the stack
                        // and call Mathf.Clamp on the temporary axis value, which is already on the stack.
                        // The instruction after the call sets the axis value, so by skipping these instructions,
                        // the axis value is set to the temporary axis value directly.

                        // The original IL looks like this:
                        // (this pointer and axis value already on stack)
                        // ldc.r4   -1
                        // ldc.r4    1
                        // call     Mathf.Clamp(float, float, float)
                        // stfld    CustomAxisState.value
                        // Not yielding the current instruction or the next two cuts the three clamp instructions
                        instructionEnumerator.MoveNext();
                        instructionEnumerator.MoveNext();
                        break;
                    }

                    yield return instruction;
                }

                // Part 2: disable speed limiting and smoothing on the horizontal and vertical axes
                // Essentially, if the axis is a camera axis (ActionID.CameraHorizontal/Vertical),
                // we set the axis value manually and branch to _after_ the speed limiting/smoothing routine
                // so that the code can still wrap or clamp the final value (which is the actual camera angle)

                var afterInvertLabel = generator.DefineLabel();
                var limitSmoothValueLabel = default(Label);

                var invertInputInfo = AccessTools.Field(typeof(CustomAxisState), "invertInput");
                while (instructionEnumerator.MoveNext())
                {
                    var instruction = instructionEnumerator.Current;
                    if (instruction.opcode == OpCodes.Ldfld && ((FieldInfo)instruction.operand).Equals(invertInputInfo))
                    {
                        // The next instruction is a branch to a label we're going to use,
                        // so yield this one and grab (and replace) the next instruction's operand.
                        // The IL code looks like this when translated to C#:
                        // if (this.invertInput) // else goto existing label
                        // {
                        //     yadda yadda;
                        // }
                        // our label: (code we want to insert)
                        // existing label: (code that will get executed if invertInput is false because the label points to it)
                        // When we replace the label, the code will jump to our new label if false and fall through to our code if true
                        yield return instruction;
                        instructionEnumerator.MoveNext();
                        var nextInstruction = instructionEnumerator.Current;
                        limitSmoothValueLabel = (Label)nextInstruction.operand;
                        nextInstruction.operand = afterInvertLabel;
                        yield return nextInstruction;
                        break;
                    }

                    yield return instruction;
                }

                // Now we need to get to the end of the above if statement block, which ends with an stloc.s
                // This just so happens to be the local we need to read to set the raw axis value
                // Hopefully, it'll all stay like this when Illusion recompiles the code after updates

                var axisValueBuilder = default(LocalBuilder);

                while (instructionEnumerator.MoveNext())
                {
                    yield return instructionEnumerator.Current;
                    if (instructionEnumerator.Current.opcode == OpCodes.Stloc_S)
                    {
                        axisValueBuilder = (LocalBuilder)instructionEnumerator.Current.operand;
                        break;
                    }
                }

                var inputAxisIDInfo = AccessTools.Field(typeof(CustomAxisState), "inputAxisID");
                // if (inputAxisID == (int)ActionID.CameraHorizontal || inputAxisID == (int)ActionID.CameraVertical)
                //     use our code
                //     goto value wrap/clamp code
                // else goto limit/smooth code
                var valueWrapStartLabel = generator.DefineLabel();
                var rawSetLabel = generator.DefineLabel();
                var firstNewInstruction = new CodeInstruction(OpCodes.Ldarg_0);
                firstNewInstruction.labels.Add(afterInvertLabel);
                yield return firstNewInstruction;
                yield return new CodeInstruction(OpCodes.Ldfld, inputAxisIDInfo);
                yield return new CodeInstruction(OpCodes.Ldc_I4, (int)ActionID.CameraHorizontal);
                yield return new CodeInstruction(OpCodes.Beq, rawSetLabel);

                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, inputAxisIDInfo);
                yield return new CodeInstruction(OpCodes.Ldc_I4, (int)ActionID.CameraVertical);
                yield return new CodeInstruction(OpCodes.Beq, rawSetLabel);
                yield return new CodeInstruction(OpCodes.Br, limitSmoothValueLabel);

                var axisValueInfo = AccessTools.Field(typeof(CustomAxisState), "value");
                var maxSpeedInfo = AccessTools.Field(typeof(CustomAxisState), "maxSpeed");
                
                // If we're at the instructions we're about to emit, we're using a mouse axis
                // this.value += <local axis value> * this.maxSpeed * 0.01f * AILookSpeedUnlocker.Sensitivity;
                var firstRawSetInstruction = new CodeInstruction(OpCodes.Ldarg_0);
                firstRawSetInstruction.labels.Add(rawSetLabel);
                yield return firstRawSetInstruction;
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Ldfld, axisValueInfo);
                yield return new CodeInstruction(OpCodes.Ldloc_S, axisValueBuilder);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldfld, maxSpeedInfo);
                yield return new CodeInstruction(OpCodes.Mul);
                yield return new CodeInstruction(OpCodes.Ldc_R4, 0.01f);
                yield return new CodeInstruction(OpCodes.Mul);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(AILookSpeedUnlocker), "Sensitivity"));
                yield return new CodeInstruction(OpCodes.Mul);
                yield return new CodeInstruction(OpCodes.Add);
                yield return new CodeInstruction(OpCodes.Stfld, axisValueInfo);
                yield return new CodeInstruction(OpCodes.Br, valueWrapStartLabel);

                // Yield the limit/smooth code until we hit the limit/wrap code
                while (instructionEnumerator.MoveNext())
                {
                    var instruction = instructionEnumerator.Current;
                    yield return instruction;
                    if (instruction.opcode == OpCodes.Stfld && ((FieldInfo)instruction.operand).Equals(axisValueInfo))
                    {
                        // The instruction after this one is the one we branch to if we're using a mouse axis
                        break;
                    }
                }

                instructionEnumerator.MoveNext();
                var valueWrapStartInstruction = instructionEnumerator.Current;
                valueWrapStartInstruction.labels.Add(valueWrapStartLabel);
                yield return valueWrapStartInstruction;

                // Yield the rest of the code
                while (instructionEnumerator.MoveNext())
                {
                    yield return instructionEnumerator.Current;
                }
            }
        }
    }
}
