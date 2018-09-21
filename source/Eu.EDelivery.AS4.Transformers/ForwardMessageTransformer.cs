﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Eu.EDelivery.AS4.Model.Internal;

namespace Eu.EDelivery.AS4.Transformers
{
    [Obsolete(
        "Due to the Dynamic Discovery in the Forwarding Steps the ToParty of the AS4Message is required, "
        + "meaning that the AS4Message has to be deserialized. Please use the " + nameof(AS4MessageTransformer) + " instead.")]
    public class ForwardMessageTransformer : ITransformer
    {
        /// <summary>
        /// Configures the <see cref="ITransformer"/> implementation with specific user-defined properties.
        /// </summary>
        /// <param name="properties">The properties.</param>
        public void Configure(IDictionary<string, string> properties) { }

        /// <summary>
        /// Transform a given <see cref="ReceivedMessage"/> to a Canonical <see cref="MessagingContext"/> instance.
        /// </summary>
        /// <param name="message">Given message to transform.</param>
        /// <returns></returns>
        public async Task<MessagingContext> TransformAsync(ReceivedMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            var context = new MessagingContext(message, MessagingContextMode.Forward);
            message.AssignPropertiesTo(context);

            return await Task.FromResult(context);
        }
    }
}