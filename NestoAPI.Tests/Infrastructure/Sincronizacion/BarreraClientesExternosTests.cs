using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// 11/11/26 (eco de Odoo, odoo-custom-addons#48): a lo que llega de fuera solo se le aplican los campos distintos que pasan la
    /// barrera. Valores inventados con la forma de los casos reales.
    /// </summary>
    [TestClass]
    public class BarreraClientesExternosTests
    {
        private static Cliente ClienteNesto() => new Cliente
        {
            Nº_Cliente = "99901     ",
            Contacto = "0  ",
            Nombre = "PELUQUERIA EJEMPLO",
            Teléfono = "916000001/600000002",
            Dirección = "CALLE FALSA, 1",
            Población = "ALGETE",
            CodPostal = "28110",
            Provincia = "MADRID",
            CIF_NIF = "B00000000",
            Comentarios = "Cierra los lunes",
            Vendedor = "JE "
        };

        private static ClienteSyncMessage Eco(Action<ClienteSyncMessage> cambiar = null)
        {
            var mensaje = new ClienteSyncMessage
            {
                Source = "Odoo",
                Cliente = "99901",
                Contacto = "0",
                Nombre = "PELUQUERIA EJEMPLO",
                Telefono = "916000001/600000002",
                Direccion = "CALLE FALSA, 1",
                Poblacion = "ALGETE",
                CodigoPostal = "28110",
                Provincia = "MADRID",
                Nif = "B00000000",
                Comentarios = "Cierra los lunes"
            };
            cambiar?.Invoke(mensaje);
            return mensaje;
        }

        private static HashSet<string> Campos(Cliente nesto, ClienteSyncMessage mensaje, List<CampoDescartadoSincronizacion> descartados = null) =>
            BarreraClientesExternos.CamposAAplicar(nesto, mensaje, descartados ?? new List<CampoDescartadoSincronizacion>());

        // ---------- Solo lo distinto ----------

        [TestMethod]
        public void SoloSeAplicanLosCamposDistintos_NoTodosLosQueLleganConContenido()
        {
            Cliente nesto = ClienteNesto();
            nesto.Nombre = "NOMBRE VIEJO";
            ClienteSyncMessage mensaje = Eco(m => m.Direccion = "calle falsa, 1 "); // igual normalizado

            HashSet<string> campos = Campos(nesto, mensaje);

            CollectionAssert.AreEquivalent(new[] { BarreraClientesExternos.CAMPO_NOMBRE }, campos.ToArray());
            nesto.Dirección = "CALLE FALSA, 1    ";
            BarreraClientesExternos.Aplicar(nesto, mensaje, campos);
            Assert.AreEqual("PELUQUERIA EJEMPLO", nesto.Nombre);
            Assert.AreEqual("CALLE FALSA, 1    ", nesto.Dirección, "la dirección no es distinta: no se toca");
            Assert.AreEqual("EXTERNAL_SYNC", nesto.Usuario);
        }

        [TestMethod]
        public void SinNadaQueAplicar_NoTocaElCliente()
        {
            Cliente nesto = ClienteNesto();
            Assert.IsFalse(BarreraClientesExternos.Aplicar(nesto, Eco(), Campos(nesto, Eco())));
            Assert.IsNull(nesto.Usuario);
        }

        // ---------- Comentarios ----------

        [TestMethod]
        public void Comentarios_EnvueltosEnHtmlConLineaTelefonosExtra_SeDescartanYSeTraza()
        {
            var descartados = new List<CampoDescartadoSincronizacion>();
            ClienteSyncMessage mensaje = Eco(m => m.Comentarios = "<p>Cierra los lunes</p>\n<p>[Teléfonos extra] 911111111</p>");

            HashSet<string> campos = Campos(ClienteNesto(), mensaje, descartados);

            Assert.AreEqual(0, campos.Count);
            CampoDescartadoSincronizacion descartado = descartados.Single();
            Assert.AreEqual(BarreraClientesExternos.CAMPO_COMENTARIOS, descartado.Campo);
            Assert.AreEqual("99901", descartado.Cliente);
            StringAssert.Contains(descartado.ToString(), "Cliente 99901-0: Comentarios descartado");
            Assert.IsFalse(descartado.ToString().Contains("911111111"), "la traza no lleva datos personales");
        }

        [TestMethod]
        public void Comentarios_SoloEnvueltosEnHtml_NoEsCambio()
        {
            var descartados = new List<CampoDescartadoSincronizacion>();
            HashSet<string> campos = Campos(ClienteNesto(), Eco(m => m.Comentarios = "<p>Cierra los lunes</p>"), descartados);

            Assert.AreEqual(0, campos.Count);
            Assert.AreEqual(0, descartados.Count, "el detector ya lo ve igual: ni siquiera se descarta");
        }

        [TestMethod]
        public void Comentarios_LineaQueEmpiezaPorCorchete_SinHtml_SeDescarta()
        {
            Assert.AreEqual(0, Campos(ClienteNesto(), Eco(m => m.Comentarios = "Cierra los lunes\n[Teléfonos extra] 911111111")).Count);
        }

        [TestMethod]
        public void Comentarios_TextoNuevoNormal_SeAplica()
        {
            CollectionAssert.AreEquivalent(new[] { BarreraClientesExternos.CAMPO_COMENTARIOS },
                Campos(ClienteNesto(), Eco(m => m.Comentarios = "Cierra los lunes y los martes")).ToArray());
        }

        // ---------- Teléfono ----------

        [TestMethod]
        public void Telefono_RecortadoAUnoDeLosQueYaTiene_SeDescarta()
        {
            var descartados = new List<CampoDescartadoSincronizacion>();
            Assert.AreEqual(0, Campos(ClienteNesto(), Eco(m => m.Telefono = "600000002"), descartados).Count);
            Assert.AreEqual(BarreraClientesExternos.MOTIVO_TELEFONO_SIN_NUEVOS, descartados.Single().Motivo);
        }

        [TestMethod]
        public void Telefono_LosMismosEnOtroOrdenYConOtrosSeparadores_SeDescarta()
        {
            Assert.AreEqual(0, Campos(ClienteNesto(), Eco(m => m.Telefono = "600 000 002 / +34 916 00 00 01")).Count);
            Assert.AreEqual(0, Campos(ClienteNesto(), Eco(m => m.Telefono = "600000002 916000001")).Count);
        }

        [TestMethod]
        public void Telefono_ConUnNumeroNuevo_SeAplica()
        {
            CollectionAssert.AreEquivalent(new[] { BarreraClientesExternos.CAMPO_TELEFONO },
                Campos(ClienteNesto(), Eco(m => m.Telefono = "916000001/699999999")).ToArray());
        }

        [TestMethod]
        public void Numeros_SoloDigitosYSinPrefijo()
        {
            CollectionAssert.AreEqual(new[] { "916000001", "600000002" }, BarreraClientesExternos.Numeros("91-600-00-01 / 0034 600 000 002").ToArray());
        }

        // ---------- Personas de contacto ----------

        private static PersonaContactoCliente PersonaNesto(string nombre = null, string correo = "ana@example.com") => new PersonaContactoCliente
        {
            Número = "1  ",
            Nombre = nombre,
            CorreoElectrónico = correo,
            Teléfono = "600000003"
        };

        [TestMethod]
        public void Persona_NombreIgualASuCorreo_SeDescarta()
        {
            var descartados = new List<CampoDescartadoSincronizacion>();
            HashSet<string> campos = BarreraClientesExternos.CamposAAplicarPersona(PersonaNesto(), new PersonaContactoSyncDTO
            {
                Id = "1", Nombre = "ana@example.com", CorreoElectronico = "ana@example.com", Telefonos = "600000003"
            }, "Odoo", "99901", "0", descartados);

            Assert.AreEqual(0, campos.Count);
            Assert.AreEqual(BarreraClientesExternos.MOTIVO_NOMBRE_ES_CORREO, descartados.Single().Motivo);
            Assert.AreEqual("1", descartados.Single().Persona);
        }

        [TestMethod]
        public void Persona_NombreIgualAlCorreoQueTieneNesto_SeDescartaAunqueNoLlegueElCorreo()
        {
            HashSet<string> campos = BarreraClientesExternos.CamposAAplicarPersona(PersonaNesto(), new PersonaContactoSyncDTO
            {
                Id = "1", Nombre = "ANA@example.com"
            }, "Odoo", "99901", "0", new List<CampoDescartadoSincronizacion>());

            Assert.IsFalse(campos.Contains(BarreraClientesExternos.CAMPO_NOMBRE));
        }

        [TestMethod]
        public void Persona_NombreDeVerdad_SeAplicaYSoloEso()
        {
            PersonaContactoCliente persona = PersonaNesto();
            var recibida = new PersonaContactoSyncDTO { Id = "1", Nombre = "Ana", CorreoElectronico = "ana@example.com", Telefonos = "600000003" };
            HashSet<string> campos = BarreraClientesExternos.CamposAAplicarPersona(persona, recibida, "Odoo", "99901", "0", new List<CampoDescartadoSincronizacion>());

            CollectionAssert.AreEquivalent(new[] { BarreraClientesExternos.CAMPO_NOMBRE }, campos.ToArray());
            Assert.IsTrue(BarreraClientesExternos.AplicarPersona(persona, recibida, campos));
            Assert.AreEqual("Ana", persona.Nombre);
        }

        // ---------- Traza en el handler ----------

        [TestMethod]
        public void Handler_TrazaCadaDescarteComoInformativo()
        {
            var traza = A.Fake<Action<Exception>>();
            var handler = new ClientesSyncHandler(traza);

            handler.TrazarDescartados(new[]
            {
                new CampoDescartadoSincronizacion { Cliente = "99901", Contacto = "0", Campo = "Comentarios", Motivo = BarreraClientesExternos.MOTIVO_COMENTARIOS_ODOO }
            });

            A.CallTo(() => traza.Invoke(A<CampoDescartadoSincronizacionException>.That.Matches(e =>
                e.Message.StartsWith("[Barrera clientes externos] Cliente 99901-0: Comentarios descartado")))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Handler_SiLaTrazaFalla_NoLanza()
        {
            var handler = new ClientesSyncHandler(_ => throw new InvalidOperationException("ELMAH caído"));
            handler.TrazarDescartados(new[] { new CampoDescartadoSincronizacion { Cliente = "1", Contacto = "0", Campo = "Teléfono", Motivo = "x" } });
        }
    }
}
