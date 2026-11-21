//a binding of any kind, by the name the major of Avalonia the package builds against gives
//it (the runtime SPEC: Avalonia 12): IBinding in 11, BindingBase in 12
#if AVALONIA12
global using AnyBinding = Avalonia.Data.BindingBase;
#else
global using AnyBinding = Avalonia.Data.IBinding;
#endif
using Avalonia.Metadata;

// the project URL is the namespace; pattech.words stays as an alias for AXAML written against it
[assembly: XmlnsDefinition("https://github.com/pzahra/words", "PatTech.Localization")]
[assembly: XmlnsDefinition("https://github.com/pzahra/words", "PatTech.Localization.Avalonia")]
[assembly: XmlnsPrefix("https://github.com/pzahra/words", "l")]
[assembly: XmlnsDefinition("pattech.words", "PatTech.Localization")]
[assembly: XmlnsDefinition("pattech.words", "PatTech.Localization.Avalonia")]
