﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Eu.EDelivery.AS4.ComponentTests.Properties {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    internal class Resources {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Resources() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Eu.EDelivery.AS4.ComponentTests.Properties.Resources", typeof(Resources).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        internal static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized resource of type System.Byte[].
        /// </summary>
        internal static byte[] deliveragent_message {
            get {
                object obj = ResourceManager.GetObject("deliveragent_message", resourceCulture);
                return ((byte[])(obj));
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to &lt;?xml version=&quot;1.0&quot; encoding=&quot;utf-8&quot;?&gt;
        ///
        ///&lt;PMode xmlns:xsi=&quot;http://www.w3.org/2001/XMLSchema-instance&quot; xmlns:xsd=&quot;http://www.w3.org/2001/XMLSchema&quot;
        ///       xmlns=&quot;eu:edelivery:as4:pmode&quot;&gt;
        ///  &lt;Id&gt;8.3.1-pmode&lt;/Id&gt;&lt;Mep&gt;OneWay&lt;/Mep&gt;&lt;MepBinding&gt;Pull&lt;/MepBinding&gt;
        ///  &lt;Reliability&gt;
        ///    &lt;DuplicateElimination&gt;
        ///      &lt;IsEnabled&gt;false&lt;/IsEnabled&gt;
        ///    &lt;/DuplicateElimination&gt;
        ///  &lt;/Reliability&gt;
        ///  &lt;ReceiptHandling&gt;
        ///    &lt;UseNNRFormat&gt;false&lt;/UseNNRFormat&gt;&lt;ReplyPattern&gt;Response&lt;/ReplyPattern&gt;&lt;SendingPMode&gt;pmode&lt;/SendingP [rest of string was truncated]&quot;;.
        /// </summary>
        internal static string deliveragent_pmode {
            get {
                return ResourceManager.GetString("deliveragent_pmode", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized resource of type System.Byte[].
        /// </summary>
        internal static byte[] receiveagent_message {
            get {
                object obj = ResourceManager.GetObject("receiveagent_message", resourceCulture);
                return ((byte[])(obj));
            }
        }
        
        /// <summary>
        ///   Looks up a localized resource of type System.Byte[].
        /// </summary>
        internal static byte[] receiveagent_message_nonexist_attachment {
            get {
                object obj = ResourceManager.GetObject("receiveagent_message_nonexist_attachment", resourceCulture);
                return ((byte[])(obj));
            }
        }
    }
}
