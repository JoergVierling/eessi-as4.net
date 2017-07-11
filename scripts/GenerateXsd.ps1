param([string]$binDirectory = "../output/Staging/bin", [string]$outputDirectory = "../output/Staging/Documentation/Schemas")

$cmd = "C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.6.1 Tools\xsd.exe"

# Export new XSD files from the build assembly
& $cmd $binDirectory/Eu.EDelivery.AS4.dll  /type:Eu.EDelivery.AS4.Model.Submit.SubmitMessage /o:$outputDirectory
Move-Item $outputDirectory/schema0.xsd $outputDirectory/submitmessage-schema.xsd -Force

& $cmd $binDirectory/Eu.EDelivery.AS4.dll  /type:Eu.EDelivery.AS4.Model.Deliver.DeliverMessage /o:$outputDirectory
Move-Item $outputDirectory/schema0.xsd $outputDirectory/delivermessage-schema.xsd -Force

& $cmd $binDirectory/Eu.EDelivery.AS4.dll  /type:Eu.EDelivery.AS4.Model.Notify.NotifyMessage /o:$outputDirectory
Move-Item $outputDirectory/schema0.xsd $outputDirectory/notifymessage-schema.xsd -Force

& $cmd $binDirectory/Eu.EDelivery.AS4.dll  /type:Eu.EDelivery.AS4.Model.PMode.SendingProcessingMode /o:$outputDirectory
Move-Item $outputDirectory/schema0.xsd $outputDirectory/send-pmode-schema.xsd -Force

& $cmd $binDirectory/Eu.EDelivery.AS4.dll  /type:Eu.EDelivery.AS4.Model.PMode.ReceivingProcessingMode /o:$outputDirectory
Move-Item $outputDirectory/schema0.xsd $outputDirectory/receive-pmode-schema.xsd -Force

# Update each 'any' element with the 'processContents' attribute set to 'lax'
Get-ChildItem $outputDirectory -Filter '*.xsd' | % {
        $content = [xml](Get-Content $_.FullName)
     
        Select-Xml $content -XPath "//*[local-name()='any']" | % {
            $element = [System.Xml.XmlElement]$_.Node
            $element.SetAttribute("processContents", "lax")
        }        
        
        $content.Save($_.FullName)
    }