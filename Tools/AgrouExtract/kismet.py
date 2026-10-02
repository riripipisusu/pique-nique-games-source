# Bytecode Kismet (bpdump d'AgrouExtract) -> pseudo-code lisible, une fonction apres l'autre.
# python kismet.py <bp.json> [fonction...] > sortie.txt
import json, sys

def name(v):
    if isinstance(v, dict):
        if "Property" in v and isinstance(v["Property"], dict): return v["Property"].get("Name", "?")
        if "ObjectName" in v: return v["ObjectName"].split("'")[-2].split(":")[-1] if "'" in v["ObjectName"] else v["ObjectName"]
        if "Name" in v: return v["Name"]
    return str(v)

def ex(e):
    if e is None: return ""
    if isinstance(e, list): return ", ".join(ex(x) for x in e)
    if not isinstance(e, dict): return str(e)
    t = e.get("Token", "")
    if t in ("EX_LocalVariable", "EX_InstanceVariable", "EX_LocalOutVariable", "EX_DefaultVariable"): return name(e.get("Variable"))
    if t in ("EX_IntConst", "EX_FloatConst", "EX_DoubleConst", "EX_ByteConst", "EX_Int64Const"): return str(e.get("Value"))
    if t in ("EX_StringConst", "EX_UnicodeStringConst", "EX_NameConst"): return repr(e.get("Value"))
    if t == "EX_TextConst": return repr(ex(e.get("Value", {}).get("SourceString")) if isinstance(e.get("Value"), dict) else e.get("Value"))
    if t == "EX_True": return "true"
    if t == "EX_False": return "false"
    if t in ("EX_Self",): return "self"
    if t in ("EX_NoObject", "EX_Nothing"): return "null"
    if t == "EX_ObjectConst": return name(e.get("Value"))
    if t in ("EX_ByteConst", "EX_IntConstByte"): return str(e.get("Value"))
    if t in ("EX_FinalFunction", "EX_VirtualFunction", "EX_CallMath", "EX_LocalFinalFunction", "EX_LocalVirtualFunction", "EX_CallMulticastDelegate"):
        f = e.get("StackNode") or e.get("Function") or e.get("VirtualFunctionName")
        return f"{name(f)}({ex(e.get('Parameters'))})"
    if t in ("EX_Context", "EX_Context_FailSilent", "EX_ClassContext"):
        return f"{ex(e.get('ObjectExpression'))}.{ex(e.get('ContextExpression'))}"
    if t == "EX_StructMemberContext": return f"{ex(e.get('StructExpression'))}.{name(e.get('Property'))}"
    if t in ("EX_Let", "EX_LetObj", "EX_LetBool", "EX_LetWeakObjPtr", "EX_LetValueOnPersistentFrame", "EX_LetMulticastDelegate", "EX_LetDelegate"):
        return f"{ex(e.get('Variable'))} = {ex(e.get('Expression') or e.get('Assignment'))}"
    if t == "EX_JumpIfNot": return f"if not ({ex(e.get('BooleanExpression'))}) goto {e.get('CodeOffset')}"
    if t == "EX_Jump": return f"goto {e.get('CodeOffset')}"
    if t == "EX_ComputedJump": return f"goto [{ex(e.get('CodeOffsetExpression'))}]"
    if t == "EX_PushExecutionFlow": return f"push {e.get('PushingAddress')}"
    if t == "EX_PopExecutionFlow": return "pop"
    if t == "EX_PopExecutionFlowIfNot": return f"pop if not ({ex(e.get('BooleanExpression'))})"
    if t == "EX_Return": return f"return {ex(e.get('ReturnExpression'))}"
    if t in ("EX_DynamicCast", "EX_MetaCast", "EX_ObjToInterfaceCast", "EX_CrossInterfaceCast", "EX_InterfaceToObjCast"): return f"cast<{name(e.get('ClassPtr'))}>({ex(e.get('Target'))})"
    if t == "EX_StructConst": return "{" + ex(e.get("Properties") or e.get("Value")) + "}"
    if t == "EX_ArrayConst" or t == "EX_SetArray": return "[" + ex(e.get("Elements") or e.get("Values")) + "]"
    if t == "EX_SkipOffsetConst": return str(e.get("Value"))
    if t in ("EX_EndOfScript", "EX_Tracepoint", "EX_WireTracepoint", "EX_InstrumentationEvent"): return ""
    if t == "EX_Skip": return ex(e.get("SkipExpression"))
    if t == "EX_BindDelegate": return f"bind {e.get('FunctionName')}"
    if t == "EX_SwitchValue": return f"switch({ex(e.get('IndexTerm'))})"
    rest = {k: v for k, v in e.items() if k not in ("Token", "StatementIndex", "ObjectPath")}
    return t.replace("EX_", "") + "(" + ", ".join(ex(v) for v in rest.values()) + ")"

d = json.load(open(sys.argv[1], encoding="utf-8"))
only = set(sys.argv[2:])
for f in d:
    if f.get("Type") != "Function" or "ScriptBytecode" not in f: continue
    if only and f["Name"] not in only: continue
    print(f"\n===== {f['Name']} =====")
    for st in f["ScriptBytecode"]:
        s = ex(st)
        if s: print(f"  {st.get('StatementIndex', ''):>6}  {s}")
