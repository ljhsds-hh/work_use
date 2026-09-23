"use strict";
/*
 * CodeFormatter 引擎冒烟测试（Node 直接跑，无需浏览器）
 *
 * 原理：从 Code/CodeFormatter.html 里切出纯引擎段（从「语言列表」注释到「UI」注释之间，
 * 不含任何 DOM 代码），落到临时文件后 require，再对各引擎做行为断言。
 * 因此它测的永远是仓库里那份真实 HTML，不会出现"脚本与页面两份代码漂移"。
 *
 * 运行：node Code/scripts/smoke-formatter.js   （退出码 0 = 全绿）
 */

const fs = require("fs");
const os = require("os");
const path = require("path");

const htmlPath = path.join(__dirname, "..", "CodeFormatter.html");
const html = fs.readFileSync(htmlPath, "utf8");

const HEAD = "/* ========== 语言列表 ========== */";
const TAIL = "/* ========== UI ========== */";
const iHead = html.indexOf(HEAD);
const iTail = html.indexOf(TAIL);
if (iHead === -1 || iTail === -1 || iTail <= iHead) {
  console.error("没能从 CodeFormatter.html 中定位引擎段（锚点注释按这个格式写：/* ========== x ========== */）");
  process.exit(2);
}
const enginesSrc = html.slice(iHead, iTail);

const tmpFile = path.join(os.tmpdir(), "codeformatter-engines-" + process.pid + ".js");
fs.writeFileSync(
  tmpFile,
  enginesSrc + "\nmodule.exports = { detectLanguage, engines, LANG_NAMES };\n"
);

let pass = 0, fail = 0;
const failures = [];

function eq(name, actual, expected) {
  if (actual === expected) { pass++; return; }
  fail++;
  failures.push(name + "\n  期望: " + JSON.stringify(expected) + "\n  实际: " + JSON.stringify(actual));
}
function ok(name, cond) {
  if (cond) { pass++; return; }
  fail++;
  failures.push(name);
}

try {
  const { detectLanguage, engines } = require(tmpFile);

  /* ---- JSON ---- */
  eq("json.format 2 空格缩进",
    engines.json.format('{"a":1,"b":[1,2]}'),
    '{\n  "a": 1,\n  "b": [\n    1,\n    2\n  ]\n}');
  eq("json.minify 紧凑输出", engines.json.minify('{\n "a": 1 \n}'), '{"a":1}');
  let jsonErrMsg = "";
  try { engines.json.format("{bad}"); } catch (e) { jsonErrMsg = e.message; }
  ok("json 非法输入报「JSON 解析失败」", /JSON 解析失败/.test(jsonErrMsg));

  /* ---- XML / XAML / HTML ---- */
  eq("xml.format 属性归一双引号 + 2 空格缩进",
    engines.xml.format('<root><a x="1" y=\'2\'>text</a></root>'),
    '<root>\n  <a x="1" y="2">text</a>\n</root>\n');
  eq("xaml.format 4 空格缩进",
    engines.xaml.format("<Grid>\n<Button/>\n</Grid>"),
    "<Grid>\n    <Button/>\n</Grid>\n");
  eq("html.format void 元素不加深度、pre 内容原样保留",
    engines.html.format('<div><img src="a.png"><pre><b>keep</b>\n  me</pre></div>'),
    '<div>\n  <img src="a.png">\n  <pre>\n<b>keep</b>\n  me\n  </pre>\n</div>\n');

  /* ---- CSS ---- */
  eq("css.format 属性冒号后单空格 + 2 空格缩进",
    engines.css.format("body{color:red;margin:0;}"),
    "body {\n  color: red;\n  margin: 0;\n}\n");
  eq("css.minify 紧凑化", engines.css.minify("body {\n  color: red; margin: 0;\n}\n"), "body{color:red;margin:0}");
  ok("css.minify 字符串字面量不受压缩影响", engines.css.minify('a:after{content:"a  b"}') === 'a:after{content:"a  b"}');

  /* ---- JavaScript ---- */
  eq("js.format K&R 花括号 + } else { 合行 + 4 空格缩进",
    engines.js.format("function f(){\nif(a){b();}else{c();}\n}"),
    "function f() {\n    if (a) {\n        b();\n    } else {\n        c();\n    }\n}\n");
  ok("js.format = 之后的正则字面量按正则处理", engines.js.format("const r = /ab+c/g;").includes("/ab+c/g"));
  ok("js.minify ASI 保护：return 后的换行不吞", engines.js.minify("function f() {\n  return\n  x;\n}\n").includes("return\n"));

  /* ---- SQL ---- */
  ok("sql.format 主子句换行 + 关键字大写",
    engines.sql.format("select id,name from t where a=1 and b=2 order by id")
      .startsWith("SELECT\n  id,\n  name\nFROM\n  t\nWHERE\n  a = 1 AND b = 2\nORDER BY\n"));
  ok("sql.minify 去注释但保留必要空格", engines.sql.minify("select 1 -- note\nfrom t") === "select 1 from t");

  /* ---- C# / Java ---- */
  ok("cs.format 单等号运算符空格 + 4 空格缩进",
    engines.cs.format('class A{void M(){var s="hi";M();}}').includes('        var s = "hi";\n'));
  ok("cs.format 逐字字符串 @\"...\" 原样保留", engines.cs.format('var p=@"C:\\temp\\n";').includes('@"C:\\temp\\n"'));
  ok("cs.format 插值字符串 $\"...\" 原样保留", engines.cs.format('var s=$"x={x}";').includes('$"x={x}"'));
  ok("cs.format 预处理指令整行保留", engines.cs.format("#region T\nclass A{}\n#endregion\n").includes("#region T"));
  ok("cs.format 复合运算符不拆散（K-1 修复）",
    engines.cs.format("if(a==b){}") === "if(a == b) {\n}\n" &&
    engines.cs.format("if(a<=b){}") === "if(a <= b) {\n}\n" &&
    engines.cs.format("if(a!=b){}") === "if(a != b) {\n}\n" &&
    engines.cs.format("x=a>=b;") === "x = a >= b;\n" &&
    engines.cs.format("if(a&&b){}") === "if(a && b) {\n}\n");
  ok("cs.format 泛型实参表不加空格（K-2 修复）",
    engines.cs.format("var l=new List<int>();") === "var l = new List<int>();\n" &&
    engines.cs.format("var l=new List<string>();") === "var l = new List<string>();\n" &&
    engines.cs.format("var d=new Dictionary<string,List<int>>();") === "var d = new Dictionary<string, List<int>>();\n" &&
    engines.cs.format("List<T> Map<T>(List<T> src){return src;}") === "List<T> Map<T>(List<T> src) {\n    return src;\n}\n");
  ok("cs.format 可空类型 int? 与条件访问 a?[0] 贴身",
    engines.cs.format("int? q=null;") === "int? q = null;\n" &&
    engines.cs.format("var v=a?[0];") === "var v = a?[0];\n" &&
    engines.cs.format("var y=a?b:c;") === "var y = a ? b : c;\n");
  ok("cs.format 括号内分号不拆行（for 头）与括号内逗号空格",
    engines.cs.format("for(int i=0;i<n;i++){s+=i;}") === "for(int i = 0; i < n; i++) {\n    s += i;\n}\n" &&
    engines.cs.format("f(a,b);") === "f(a, b);\n");
  ok("cs.format 后缀自增与一元负号",
    engines.cs.format("i++;") === "i++;\n" &&
    engines.cs.format("a=-b;") === "a = -b;\n");
  ok("cs.format } 捕获链与收尾链同行",
    engines.cs.format("try{M();}catch(Exception e){Log(e);}finally{C();}") ===
      "try {\n    M();\n} catch(Exception e) {\n    Log(e);\n} finally {\n    C();\n}\n" &&
    engines.cs.format("list.ForEach(x=>{M(x);});") === "list.ForEach(x => {\n    M(x);\n});\n");
  ok("java.format 泛型与基础排版",
    engines.java.format("Map<String,List<Integer>> m=new HashMap<>();") === "Map<String, List<Integer>> m = new HashMap<>();\n" &&
    engines.java.format("class A{void M(){int x=1;}}").includes("    int x = 1;"));
  ok("cs/java.minify 泛型闭合后保留空格",
    engines.cs.minify("List<int> x;") === "List<int> x;" &&
    engines.java.minify("List<String> l;") === "List<String> l;");

  /* ---- 自动识别 ---- */
  eq("detect json", detectLanguage('{"a":1}'), "json");
  eq("detect xaml", detectLanguage('<UserControl xmlns:x="http://x" x:Class="A.B"></UserControl>'), "xaml");
  eq("detect html", detectLanguage("<!DOCTYPE html>\n<html><body></body></html>"), "html");
  eq("detect xml", detectLanguage('<?xml version="1.0"?>\n<root><child/></root>'), "xml");
  eq("detect sql", detectLanguage("SELECT * FROM t WHERE a=1"), "sql");
  eq("detect cs", detectLanguage('using System;\nnamespace N { class A { var x = new object(); } }'), "cs");
  eq("detect java", detectLanguage("package a.b;\nimport java.util.List;\npublic class A { public static void main(String[] args) {} }"), "java");
  eq("detect css", detectLanguage(".a{color:red;}"), "css");
  eq("detect js", detectLanguage("const x = 1; console.log(x);"), "js");
  eq("detect 空白输入返回 null", detectLanguage("   "), null);
} finally {
  fs.unlinkSync(tmpFile);
}

console.log("CodeFormatter 引擎冒烟：通过 " + pass + "，失败 " + fail);
if (fail) {
  console.error("\n失败明细：\n" + failures.map(s => "  - " + s).join("\n"));
  process.exit(1);
}
