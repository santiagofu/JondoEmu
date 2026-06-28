using System;
using System.Collections.Generic;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Jondo.Unity.Protocol.Messages;

namespace Jondo.Unity.Protocol
{
    public static class TypeRegistry
    {
        private static readonly Dictionary<string, MessageDescriptor> AliasToDescriptor = new();
        private static readonly Dictionary<Type, string> TypeToAlias = new();

        static TypeRegistry()
        {
            Register("ise", GameMapMovementRequestMessage.Descriptor);
            Register("hhf", hhf.Descriptor);
            Register("hhh", hhh.Descriptor);
            Register("hmt", hmt.Descriptor);
            Register("ilc", ilc.Descriptor);
            Register("iry", iry.Descriptor);
            Register("isf", isf.Descriptor);
            Register("isi", isi.Descriptor);
            Register("jnx", jnx.Descriptor);
            Register("joa", joa.Descriptor);
            Register("jog", jog.Descriptor);
            Register("joh", joh.Descriptor);
            Register("joi", joi.Descriptor);
            Register("jol", jol.Descriptor);
            Register("joo", joo.Descriptor);
            Register("jos", jos.Descriptor);
            Register("jpb", jpb.Descriptor);
            Register("jpg", jpg.Descriptor);
            Register("jpj", jpj.Descriptor);
            Register("jpp", jpp.Descriptor);
            Register("jps", jps.Descriptor);
            Register("jpv", jpv.Descriptor);
            Register("jqb", jqb.Descriptor);
            Register("kkr", kkr.Descriptor);
            Register("kku", kku.Descriptor);
            Register("kns", kns.Descriptor);
            Register("knx", knx.Descriptor);
            Register("kod", kod.Descriptor);
            Register("kpa", kpa.Descriptor);
            Register("kpc", kpc.Descriptor);
            Register("kqn", kqn.Descriptor);
            Register("kqp", kqp.Descriptor);
            Register("krb", krb.Descriptor);
            Register("krc", krc.Descriptor);
            Register("kri", kri.Descriptor);
            Register("ksl", ksl.Descriptor);
            Register("ksq", ksq.Descriptor);
            Register("ksx", ksx.Descriptor);
            Register("ktw", ktw.Descriptor);
            Register("lai", lai.Descriptor);
            Register("lar", lar.Descriptor);
            Register("lcd", lcd.Descriptor);
            Register("lcj", lcj.Descriptor);
            Register("lct", lct.Descriptor);
            Register("lep", lep.Descriptor);
            Register("ley", ley.Descriptor);
            Register("lfj", lfj.Descriptor);
            Register("lfo", lfo.Descriptor);
            Register("lfx", lfx.Descriptor);
            Register("lgz", lgz.Descriptor);
            Register("lhi", lhi.Descriptor);
            Register("lhr", lhr.Descriptor);
            Register("lhy", lhy.Descriptor);
            Register("lif", lif.Descriptor);
            Register("ljk", ljk.Descriptor);
            Register("lkr", lkr.Descriptor);
            Register("lkt", lkt.Descriptor);
            Register("lnk", lnk.Descriptor);
            Register("loy", loy.Descriptor);
            Register("lpj", lpj.Descriptor);
            Register("lsy", lsy.Descriptor);
            Register("luq", luq.Descriptor);
            Register("luy", luy.Descriptor);
            Register("lxd", lxd.Descriptor);
        }

        private static void Register(string alias, MessageDescriptor descriptor)
        {
            AliasToDescriptor[alias] = descriptor;
            TypeToAlias[descriptor.ClrType] = alias;
        }

        public static MessageDescriptor GetDescriptorByAlias(string alias)
        {
            if (AliasToDescriptor.TryGetValue(alias, out var descriptor))
            {
                return descriptor;
            }
            return null;
        }

        public static string GetAliasByType(Type type)
        {
            if (TypeToAlias.TryGetValue(type, out var alias))
            {
                return alias;
            }
            return null;
        }

        /// <summary>
        /// Creates an Any message with the correct type.ankama.com URL
        /// </summary>
        public static Google.Protobuf.WellKnownTypes.Any Pack<T>(T message) where T : IMessage<T>
        {
            var alias = GetAliasByType(typeof(T));
            if (alias == null)
            {
                throw new Exception($"Message {typeof(T).Name} is not registered with an alias.");
            }

            return new Google.Protobuf.WellKnownTypes.Any
            {
                TypeUrl = $"type.ankama.com/{alias}",
                Value = message.ToByteString()
            };
        }
    }
}
