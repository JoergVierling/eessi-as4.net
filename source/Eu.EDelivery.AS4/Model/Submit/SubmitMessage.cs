﻿using System.Xml;
using System.Xml.Serialization;
using Eu.EDelivery.AS4.Model.Common;
using Eu.EDelivery.AS4.Model.PMode;
using CollaborationInfo = Eu.EDelivery.AS4.Model.Common.CollaborationInfo;
using MessageProperty = Eu.EDelivery.AS4.Model.Common.MessageProperty;
using PartyInfo = Eu.EDelivery.AS4.Model.Common.PartyInfo;

namespace Eu.EDelivery.AS4.Model.Submit
{
    [XmlRoot(Namespace = "urn:cef:edelivery:eu:as4:messages")]
    public class SubmitMessage
    {
        public MessageInfo MessageInfo { get; set; }

        public PartyInfo PartyInfo { get; set; }

        [XmlElement(IsNullable = true)]
        public CollaborationInfo Collaboration { get; set; }

        public MessageProperty[] MessageProperties { get; set; }

        public Payload[] Payloads { get; set; }
        
        [XmlIgnore]
        public SendingProcessingMode PMode { get; set; }

        public bool HasPayloads => Payloads != null && Payloads?.Length != 0;

        /// <summary>
        /// SamlToken Element.
        /// </summary>
        public XmlElement SamlToken { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SubmitMessage"/> class. 
        /// Create Submit Message
        /// </summary>
        public SubmitMessage()
        {            
            MessageInfo = new MessageInfo();
            Collaboration = new CollaborationInfo();
            Payloads = new Payload[] { };
            PartyInfo = new PartyInfo();
        }
    }
}