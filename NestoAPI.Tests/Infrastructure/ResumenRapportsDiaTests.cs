using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// El correo diario de rapports citaba clientes sin decir de qué vendedor eran y se dejaba
    /// vendedores sin nombrar, sin que se supiera si no habían trabajado o si no había nada que
    /// contar. Lo que el jefe de ventas necesita siempre va ahora en una cabecera calculada, y a la
    /// IA se le da cada rapport con el nombre del vendedor y del cliente.
    /// </summary>
    [TestClass]
    public class ResumenRapportsDiaTests
    {
        private static readonly DateTime FECHA = new DateTime(2026, 9, 29);

        private static IDictionary<string, ResumenRapportsDia.FichaVendedor> Fichas()
        {
            return ResumenRapportsDia.IndexarFichas(new[]
            {
                new ResumenRapportsDia.FichaVendedor { Numero = "JE ", Nombre = "Jesús", Estado = Constantes.Vendedores.ESTADO_VENDEDOR_PRESENCIAL },
                new ResumenRapportsDia.FichaVendedor { Numero = "DV ", Nombre = "David", Estado = Constantes.Vendedores.ESTADO_VENDEDOR_PRESENCIAL },
                new ResumenRapportsDia.FichaVendedor { Numero = "ISR", Nombre = "Inés Sánchez Redondo", Estado = Constantes.Vendedores.ESTADO_VENDEDOR_PRESENCIAL },
                new ResumenRapportsDia.FichaVendedor { Numero = "LHY", Nombre = "Laura", Estado = Constantes.Vendedores.ESTADO_VENDEDOR_TELEFONICO },
                new ResumenRapportsDia.FichaVendedor { Numero = "IF ", Nombre = "Isabel", Estado = Constantes.Vendedores.ESTADO_VENDEDOR_PELUQUERIA }
            });
        }

        private static ResumenRapportsDia.Rapport Rapport(string vendedor, string cliente, string vendedorCliente,
            string comentarios = "Visita al cliente para enseñar novedades", string tipo = "V", bool pedido = false)
        {
            return new ResumenRapportsDia.Rapport
            {
                Vendedor = vendedor,
                Cliente = cliente,
                Contacto = "0  ",
                NombreCliente = "CENTRO " + cliente.Trim(),
                VendedorCliente = vendedorCliente,
                Tipo = tipo,
                Comentarios = comentarios,
                Pedido = pedido
            };
        }

        [TestMethod]
        public void NombreVendedor_ConFicha_DaNombreYCodigo()
        {
            // En la BD el código viene relleno con espacios (char(3))
            Assert.AreEqual("Jesús (JE)", ResumenRapportsDia.NombreVendedor("JE ", Fichas()));
        }

        [TestMethod]
        public void NombreVendedor_SinFicha_DaElCodigo()
        {
            Assert.AreEqual("XX", ResumenRapportsDia.NombreVendedor("XX ", Fichas()));
        }

        [TestMethod]
        public void TextoParaIA_CadaRapportLlevaElNombreDelVendedorYDelCliente()
        {
            var rapports = new[] { Rapport("JE ", "12345     ", "JE ") };

            string texto = ResumenRapportsDia.TextoParaIA(FECHA, rapports, Fichas());

            StringAssert.Contains(texto, "Vendedor: Jesús (JE)");
            StringAssert.Contains(texto, "Cliente: 12345/0 CENTRO 12345");
            StringAssert.Contains(texto, "Tipo: Visita");
            StringAssert.Contains(texto, "Terminó en pedido: No");
        }

        [TestMethod]
        public void TextoParaIA_TipoContactoYNombreNull_NoRevienta()
        {
            // NestoAPI#374: un solo registro cojo tumbaba el correo de todo el día con un NRE
            var rapports = new[]
            {
                new ResumenRapportsDia.Rapport { Vendedor = "JE", Cliente = "12345", Comentarios = "Comentario suficientemente largo" }
            };

            string texto = ResumenRapportsDia.TextoParaIA(FECHA, rapports, Fichas());

            StringAssert.Contains(texto, "Tipo: Desconocido");
            StringAssert.Contains(texto, "Cliente: 12345/");
        }

        [TestMethod]
        public void TextoParaIA_VendedorSoloConComentariosVacios_SaleIgualmente()
        {
            // Para que la IA pueda decir «sin nada destacable» en vez de callarse al vendedor
            var rapports = new[] { Rapport("DV ", "222", "DV ", comentarios: "nc"), Rapport("DV ", "333", "DV ", comentarios: null) };

            string texto = ResumenRapportsDia.TextoParaIA(FECHA, rapports, Fichas());

            StringAssert.Contains(texto, "VENDEDOR: David (DV). Rapports: 2, de ellos con comentario: 0");
            Assert.IsFalse(texto.Contains("Comentario: nc"));
        }

        [TestMethod]
        public void TextoParaIA_PresencialEnClienteDeTelefonico_LoAvisa()
        {
            var rapports = new[] { Rapport("JE ", "12345", "LHY") };

            string texto = ResumenRapportsDia.TextoParaIA(FECHA, rapports, Fichas());

            StringAssert.Contains(texto, "OJO: este cliente no es suyo, lo lleva Laura (LHY)");
        }

        [TestMethod]
        public void EsDeClienteDeTelefonico_PresencialEnClienteDeTelefonico_True()
        {
            Assert.IsTrue(ResumenRapportsDia.EsDeClienteDeTelefonico(Rapport("JE ", "1", "LHY"), Fichas()));
        }

        [TestMethod]
        public void EsDeClienteDeTelefonico_ClientePropioODeOtroPresencial_False()
        {
            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeTelefonico(Rapport("JE ", "1", "JE "), Fichas()));
            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeTelefonico(Rapport("JE ", "1", "DV "), Fichas()));
        }

        [TestMethod]
        public void EsDeClienteDeTelefonico_ElQueMeteElRapportNoEsPresencial_False()
        {
            // Lo raro es que lo meta un presencial: peluquería o telefónico sobre un cliente de telefónico, no
            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeTelefonico(Rapport("IF ", "1", "LHY"), Fichas()));
            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeTelefonico(Rapport("LHY", "1", "LHY"), Fichas()));
        }

        [TestMethod]
        public void EsDeClienteDeTelefonico_ClienteSinVendedorOVendedorDesconocido_False()
        {
            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeTelefonico(Rapport("JE ", "1", null), Fichas()));
            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeTelefonico(Rapport("JE ", "1", "ZZ "), Fichas()));
        }

        [TestMethod]
        public void Actividad_CuentaPorVendedorYTipo()
        {
            var rapports = new[]
            {
                Rapport("JE ", "1", "JE ", tipo: "V", pedido: true),
                Rapport("JE ", "2", "JE ", tipo: "T"),
                Rapport("JE", "3", "JE ", tipo: "W", comentarios: "nc"),
                Rapport("DV ", "4", "DV ", tipo: "V")
            };

            List<ResumenRapportsDia.ActividadVendedor> actividad = ResumenRapportsDia.Actividad(rapports);

            Assert.AreEqual(2, actividad.Count);
            ResumenRapportsDia.ActividadVendedor je = actividad.Single(a => a.Vendedor == "JE");
            Assert.AreEqual(3, je.Rapports);
            Assert.AreEqual(1, je.Visitas);
            Assert.AreEqual(1, je.Telefono);
            Assert.AreEqual(1, je.WhatsApp);
            Assert.AreEqual(1, je.ConPedido);
            Assert.AreEqual(1, je.SinComentario);
        }

        [TestMethod]
        public void SinRapports_DevuelveLosEsperadosQueNoHanMetidoNinguno()
        {
            var rapports = new[] { Rapport("JE ", "1", "JE "), Rapport("DV ", "2", "DV ") };

            List<string> sinRapports = ResumenRapportsDia.SinRapports(new[] { "DV", "ISR", "JE" }, rapports);

            CollectionAssert.AreEqual(new[] { "ISR" }, sinRapports);
        }

        [TestMethod]
        public void CabeceraHtml_NombraALosQueNoHanMetidoRapports()
        {
            var rapports = new[] { Rapport("JE ", "1", "JE ") };

            string html = ResumenRapportsDia.CabeceraHtml(FECHA, rapports, Fichas(), new[] { "JE", "ISR" });

            StringAssert.Contains(html, "Actividad del día 29/09/2026");
            StringAssert.Contains(html, "Jes&#250;s (JE)");
            StringAssert.Contains(html, "Sin ningún rapport este día:");
            StringAssert.Contains(html, "In&#233;s S&#225;nchez Redondo (ISR)");
        }

        [TestMethod]
        public void CabeceraHtml_TodosHanMetidoRapports_NoPoneElApartado()
        {
            var rapports = new[] { Rapport("JE ", "1", "JE ") };

            string html = ResumenRapportsDia.CabeceraHtml(FECHA, rapports, Fichas(), new[] { "JE" });

            Assert.IsFalse(html.Contains("Sin ningún rapport"));
            Assert.IsFalse(html.Contains("clientes de otro vendedor"));
        }

        [TestMethod]
        public void CabeceraHtml_PresencialEnClienteDeTelefonico_LoLista()
        {
            var rapports = new[] { Rapport("JE ", "12345", "LHY", pedido: true) };

            string html = ResumenRapportsDia.CabeceraHtml(FECHA, rapports, Fichas(), new[] { "JE" });

            StringAssert.Contains(html, "Rapports a clientes de otro vendedor");
            StringAssert.Contains(html, "cliente 12345/0 CENTRO 12345, que lleva Laura (LHY) (visita, termin&#243; en pedido)");
        }

        [TestMethod]
        public void CabeceraHtml_NombreDeClienteConHtml_SeEscapa()
        {
            var rapport = Rapport("JE ", "12345", "LHY");
            rapport.NombreCliente = "UÑAS <B> & CO";

            string html = ResumenRapportsDia.CabeceraHtml(FECHA, new[] { rapport }, Fichas(), new string[0]);

            Assert.IsFalse(html.Contains("<B>"));
            StringAssert.Contains(html, "&lt;B&gt; &amp; CO");
        }

        // Carlos, 30/09/26: los rapports sin vendedor son los que mete alguien de un cliente que no
        // lleva él (Daniel, usuario DLopez y vendedor DLS, de un cliente de María José).

        private static readonly Dictionary<string, string> VENDEDOR_POR_USUARIO =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DLopez"] = "DV ", ["Jesus"] = "JE" };

        [TestMethod]
        public void DeducirVendedores_RapportSinVendedor_TomaElDelUsuarioQueLoTecleo()
        {
            ResumenRapportsDia.Rapport rapport = Rapport(null, "12345", "LHY");
            rapport.Usuario = @"NUEVAVISION\dlopez";

            ResumenRapportsDia.DeducirVendedores(new[] { rapport }, VENDEDOR_POR_USUARIO);

            Assert.AreEqual("DV", rapport.Vendedor);
            Assert.IsTrue(rapport.VendedorDeducidoDelUsuario);
        }

        [TestMethod]
        public void DeducirVendedores_RapportConVendedor_NoSeToca()
        {
            ResumenRapportsDia.Rapport rapport = Rapport("JE ", "12345", "JE ");
            rapport.Usuario = @"NUEVAVISION\dlopez";

            ResumenRapportsDia.DeducirVendedores(new[] { rapport }, VENDEDOR_POR_USUARIO);

            Assert.AreEqual("JE ", rapport.Vendedor);
            Assert.IsFalse(rapport.VendedorDeducidoDelUsuario);
        }

        [TestMethod]
        public void DeducirVendedores_UsuarioDeOficinaSinVendedor_SeEnsenaSuUsuario()
        {
            ResumenRapportsDia.Rapport rapport = Rapport(null, "12345", "NV ");
            rapport.Usuario = @"NUEVAVISION\Enrique";

            ResumenRapportsDia.DeducirVendedores(new[] { rapport }, VENDEDOR_POR_USUARIO);
            string html = ResumenRapportsDia.CabeceraHtml(FECHA, new[] { rapport }, Fichas(), new string[0]);
            string paraLaIA = ResumenRapportsDia.TextoParaIA(FECHA, new[] { rapport }, Fichas());

            Assert.IsNull(rapport.Vendedor);
            StringAssert.Contains(html, "Enrique (sin vendedor)");
            StringAssert.Contains(paraLaIA, "Vendedor: Enrique (sin vendedor)");
        }

        [TestMethod]
        public void CabeceraHtml_RapportSinVendedorDeUnClienteDeOtro_SaleEnSuVendedorYEnLaListaDeClientesDeOtro()
        {
            // Aunque el cliente sea de otro presencial: si se guardó sin vendedor, no era suyo
            ResumenRapportsDia.Rapport rapport = Rapport(null, "12345", "JE ");
            rapport.Usuario = "DLopez";
            ResumenRapportsDia.DeducirVendedores(new[] { rapport }, VENDEDOR_POR_USUARIO);

            string html = ResumenRapportsDia.CabeceraHtml(FECHA, new[] { rapport }, Fichas(), new[] { "DV" });

            StringAssert.Contains(html, "David (DV)");
            Assert.IsFalse(html.Contains("(sin vendedor)"));
            StringAssert.Contains(html, "Rapports a clientes de otro vendedor");
            StringAssert.Contains(html, "que lleva Jes&#250;s (JE)");
        }

        [TestMethod]
        public void EsDeClienteDeOtroVendedor_SinVendedorPeroElClienteEsSuyo_False()
        {
            // Pasa: María José mete rapports sin vendedor de clientes que sí lleva ella
            ResumenRapportsDia.Rapport rapport = Rapport(null, "12345", "JE");
            rapport.Usuario = "Jesus";
            ResumenRapportsDia.DeducirVendedores(new[] { rapport }, VENDEDOR_POR_USUARIO);

            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeOtroVendedor(rapport, Fichas()));
        }

        [TestMethod]
        public void EsDeClienteDeOtroVendedor_ElClienteEsDelVendedorGeneral_False()
        {
            // NV no es «otro vendedor»: es un cliente que no lleva nadie (los de peluquería, en estética)
            ResumenRapportsDia.Rapport rapport = Rapport(null, "12345", "NV ");
            rapport.Usuario = "Jesus";
            ResumenRapportsDia.DeducirVendedores(new[] { rapport }, VENDEDOR_POR_USUARIO);

            Assert.IsFalse(ResumenRapportsDia.EsDeClienteDeOtroVendedor(rapport, Fichas()));
        }

        [TestMethod]
        public void LimpiarHtmlDeIA_QuitaElBloqueDeCodigo()
        {
            Assert.AreEqual("<h2>Hola</h2>", ResumenRapportsDia.LimpiarHtmlDeIA("```html\n<h2>Hola</h2>\n```"));
            Assert.AreEqual("<h2>Hola</h2>", ResumenRapportsDia.LimpiarHtmlDeIA("<h2>Hola</h2>"));
            Assert.AreEqual(string.Empty, ResumenRapportsDia.LimpiarHtmlDeIA(null));
        }
    }
}
