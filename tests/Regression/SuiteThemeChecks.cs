using ReplayMod.UI;

public static class SuiteThemeChecks
{
    public static int Run()
    {
        int passed=0;
        Test("native palette retains the released green colours",()=>
        {
            var palette=SuiteThemePalette.Build(new SuiteThemeSettings());
            Check(palette.Panel.Hex=="#050A05"&&palette.Button.Hex=="#3E463C"&&palette.Selected.Hex=="#173021","native surfaces");
            Check(palette.Accent.Hex=="#58E187"&&palette.Text.Hex=="#E0E0E0"&&palette.Muted.Hex=="#7DAE7D","native lettering");
            Check(palette.Native&&Math.Abs(palette.Opacity-248f/255)<.000001,"native opacity");
        });
        Test("six-digit hex accepts normal pasted values and rejects malformed input",()=>
        {
            foreach(string value in new[]{"#58E187","58e187","  #58e187  "})Check(SuiteRgb.TryParse(value,out var parsed)&&parsed.Hex=="#58E187","valid RGB");
            foreach(string value in new[]{null,"","#","FFF","#12345","1234567","#GG0000","#123456FF","<b>fff</b>","0x123456","##123456"})
                Check(!SuiteRgb.TryParse(value,out _),"malformed RGB rejected");
            Check(SuiteRgb.Parse("broken","#050A05").Hex=="#050A05","invalid values use bounded defaults");
        });
        Test("preset and opacity normalization handles hand-edited config",()=>
        {
            Check(SuiteThemeSettings.NormalPreset("bLuE")=="Blue"&&SuiteThemeSettings.NormalPreset("unrecognised")=="Native","preset fallback");
            Check(SuiteThemeSettings.NormalOpacity(-10)==.65f&&SuiteThemeSettings.NormalOpacity(10)==1,"opacity bounds");
            foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})
                Check(SuiteThemeSettings.NormalOpacity(value)==SuiteThemeSettings.NativeOpacity,"nonfinite opacity fallback");
            var value2=SuiteThemeSettings.Create("Custom","x",null,"#GGGGGG","#fff",float.NaN);
            Check(value2.Accent=="#58E187"&&value2.Panel=="#050A05"&&value2.Button=="#3E463C"&&value2.Text=="#E0E0E0","per-field safe fallback");
        });
        Test("every preset preserves readable panel, button and selected labels",()=>
        {
            foreach(string name in new[]{"Native","Amber","Blue","Neutral"})
            {
                var settings=new SuiteThemeSettings {Preset=name};var palette=SuiteThemePalette.Build(settings);
                VerifyContrast(palette);
                Check(palette.Native==(name=="Native"),"tints only use native mode for native preset");
            }
        });
        Test("arbitrary equal, dark, light and saturated custom colours keep reset and labels readable",()=>
        {
            string[] colours={"#000000","#FFFFFF","#777777","#FF0000","#00FF00","#0000FF","#FFFF00","#58E187"};
            foreach(string panel in colours)foreach(string button in colours)foreach(string accent in colours)foreach(string text in colours)
                VerifyContrast(SuiteThemePalette.Build(SuiteThemeSettings.Create("Custom",accent,panel,button,text,.65f)));
            var grey=SuiteThemeSettings.Create("Custom","#777777","#777777","#777777","#FFFFFF",.65f);
            var protectedGrey=SuiteThemePalette.Build(grey);VerifyContrast(protectedGrey);
            Check(protectedGrey.Opacity>.65f&&grey.Opacity==.65f,"mid-grey panels gain derived opacity without overwriting the preference");
        });
        Test("custom settings roundtrip and palette creation leave preferences intact",()=>
        {
            var settings=SuiteThemeSettings.Create("Custom","#abcdef","#010203","#567890","#000000",.8f);
            string before=settings.Signature;var palette=SuiteThemePalette.Build(settings);
            Check(settings.Signature==before&&settings.Text=="#000000","contrast correction does not overwrite chosen text colour");
            var copy=settings.Copy();Check(copy.Signature==before&&!ReferenceEquals(settings,copy),"independent settings snapshot");
            copy.Panel="#FFFFFF";Check(settings.Panel=="#010203","editing a draft cannot mutate saved preferences");
        });
        Test("contrast helper has correct luminance endpoints and stable finite mixing",()=>
        {
            var black=new SuiteRgb(0,0,0);var white=new SuiteRgb(255,255,255);
            Check(Math.Abs(SuiteRgb.Contrast(black,white)-21)<1e-10,"black and white contrast ratio");
            Check(SuiteRgb.Contrast(white,white)==1&&SuiteRgb.Contrast(black,black)==1,"same-colour contrast ratio");
            Check(SuiteRgb.Mix(black,white,-10).Hex=="#000000"&&SuiteRgb.Mix(black,white,10).Hex=="#FFFFFF","mix bounds");
            Check(SuiteRgb.Mix(black,white,.5).Hex=="#808080","rounded RGB midpoint");
        });
        Console.WriteLine($"Suite theme audit: {passed} groups, 0 failures");return passed;
        void Test(string name,Action action){action();passed++;Console.WriteLine("PASS suite theme "+name);}
    }
    private static void VerifyContrast(SuiteThemePalette palette)
    {
        Check(SuiteRgb.Contrast(palette.Text,palette.Panel)>=4.5,"panel labels readable");
        Check(SuiteRgb.Contrast(palette.Muted,palette.Panel)>=4.5,"secondary labels readable");
        Check(SuiteRgb.Contrast(palette.ButtonText,palette.Button)>=4.5,"button labels and reset readable");
        Check(SuiteRgb.Contrast(palette.SelectedText,palette.Selected)>=4.5,"selected choices readable");
        Check(SuiteRgb.Contrast(palette.Handle,palette.Button)>=4.5,"slider and scrollbar handle readable");
        Check(SuiteRgb.Contrast(palette.TrackAccent,palette.Button)>=4.5,"slider fills and text caret visible");
        Check(SuiteRgb.Contrast(palette.MarkerAccent,palette.Panel)>=4.5,"manual bookmarks visible");
        foreach(var scene in new[]{new SuiteRgb(0,0,0),new SuiteRgb(255,255,255)})
        {
            var composite=SuiteRgb.Mix(scene,palette.Panel,palette.Opacity);
            Check(SuiteRgb.Contrast(palette.Text,composite)>=4.5,"panel labels readable over scene extremes");
            Check(SuiteRgb.Contrast(palette.Muted,composite)>=4.5,"secondary labels readable over scene extremes");
            Check(SuiteRgb.Contrast(palette.MarkerAccent,composite)>=4.5,"manual bookmarks readable over scene extremes");
        }
        foreach(float tint in new[]{palette.HoverMultiplier,palette.PressedMultiplier})
        {
            Check(SuiteRgb.Contrast(palette.ButtonText,palette.Button.Tint(tint))>=4.5,"hover and pressed button labels readable");
            Check(SuiteRgb.Contrast(palette.SelectedText,palette.Selected.Tint(tint))>=4.5,"hover and pressed selected labels readable");
            Check(SuiteRgb.Contrast(palette.Handle.Tint(tint),palette.Button)>=4.5,"hover and pressed handles readable");
        }
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
