using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#874 (corte 3): Clientes.Direccion2 y TipoLugar viajan a Odoo en el mensaje de clientes (Direccion2, TipoLugar, al lado de
    /// Direccion, en cada contacto). Solo de ida: lo que traiga Odoo se ignora. Ausente = no tocar: sin columnas o sin Dirección 2 no viajan.
    /// </summary>
    [TestClass]
    public class Direccion2SincronizacionTests
    {
        private static readonly FechasComprasCliente SIN_COMPRAS = new FechasComprasCliente();

        private static Dictionary<string, Direccion2Contacto> Filas(params (string Cliente, string Contacto, string Direccion2, string TipoLugar)[] filas) =>
            filas.ToDictionary(f => Direccion2ClienteSql.ClaveContacto(f.Cliente, f.Contacto),
                f => new Direccion2Contacto { Cliente = f.Cliente, Contacto = f.Contacto, Direccion2 = f.Direccion2, TipoLugar = f.TipoLugar });

        private static (GestorClientes Gestor, List<ClienteSyncMessage> Mensajes) Gestor(IServicioGestorClientes servicio)
        {
            ISincronizacionEventPublisher publisher = A.Fake<ISincronizacionEventPublisher>();
            var mensajes = new List<ClienteSyncMessage>();
            A.CallTo(() => publisher.PublishEventAsync("sincronizacion-tablas", A<object>.Ignored))
                .Invokes((string _, object message) => mensajes.Add(message as ClienteSyncMessage));
            A.CallTo(() => servicio.LeerFechasCompras(A<IEnumerable<string>>._)).Returns(new Dictionary<string, FechasComprasCliente>());
            return (new GestorClientes(servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(publisher)), mensajes);
        }

        private static Cliente Contacto(string numero, string contacto, bool principal) =>
            new Cliente { Empresa = "1  ", Nº_Cliente = numero + "     ", Contacto = contacto + "  ", ClientePrincipal = principal, Nombre = "PELUQUERIA EJEMPLO", Dirección = "CALLE FALSA, 1" };

        private static JsonElement Json(ClienteSyncMessage mensaje) => JsonDocument.Parse(JsonSerializer.Serialize(mensaje)).RootElement;

        [TestMethod]
        public async Task ElPrincipalYUnContacto_LlevanSuDireccion2YSuTipo()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._))
                .Returns(Filas(("42146", "0", "3º BD", "Piso"), ("42146", "1", "LOCAL 6 · RÓTULO: EL EDÉN", "Local")));
            var (gestor, mensajes) = Gestor(servicio);

            await gestor.PublicarClienteSincronizar(Contacto("42146", "0", true), SIN_COMPRAS, "Nesto viejo", null, null);
            await gestor.PublicarClienteSincronizar(Contacto("42146", "1", false), SIN_COMPRAS, "Nesto viejo", null, null);

            Assert.AreEqual("3º BD", mensajes[0].Direccion2);
            Assert.AreEqual("Piso", mensajes[0].TipoLugar);
            JsonElement contacto = Json(mensajes[1]);
            Assert.AreEqual("LOCAL 6 · RÓTULO: EL EDÉN", contacto.GetProperty("Direccion2").GetString());
            Assert.AreEqual("Local", contacto.GetProperty("TipoLugar").GetString());
            Assert.AreEqual("CALLE FALSA, 1", contacto.GetProperty("Direccion").GetString());
            // Los contactos del mismo cliente seguidos (job de Nesto_sync): UNA lectura
            A.CallTo(() => servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SinDireccion2_LasPropiedadesNoViajan()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._))
                .Returns(Filas(("42146", "0", null, null), ("42146", "1", "   ", "")));
            var (gestor, mensajes) = Gestor(servicio);

            await gestor.PublicarClienteSincronizar(Contacto("42146", "0", true), SIN_COMPRAS, "Nesto viejo", null, null);
            await gestor.PublicarClienteSincronizar(Contacto("42146", "1", false), SIN_COMPRAS, "Nesto viejo", null, null);

            foreach (ClienteSyncMessage mensaje in mensajes)
            {
                JsonElement json = Json(mensaje);
                Assert.IsFalse(json.TryGetProperty("Direccion2", out _), "Ausente = Odoo no toca nada (nunca \"\")");
                Assert.IsFalse(json.TryGetProperty("TipoLugar", out _));
            }
        }

        [TestMethod]
        public async Task SinLasColumnas_ComoHoy()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._)).Returns((Dictionary<string, Direccion2Contacto>)null);
            var (gestor, mensajes) = Gestor(servicio);

            await gestor.PublicarClienteSincronizar(Contacto("42146", "0", true));

            JsonElement json = Json(mensajes.Single());
            Assert.IsFalse(json.TryGetProperty("Direccion2", out _));
            Assert.IsFalse(json.TryGetProperty("TipoLugar", out _));
            Assert.AreEqual("CALLE FALSA, 1", json.GetProperty("Direccion").GetString());
        }

        [TestMethod]
        public async Task SiFallaLaLectura_SePublicaSinElla()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._)).Throws(new InvalidOperationException("caída"));
            var (gestor, mensajes) = Gestor(servicio);

            await gestor.PublicarClienteSincronizar(Contacto("42146", "0", true));

            Assert.IsNull(mensajes.Single().Direccion2);
        }

        /// <summary>
        /// El alta (CrearCliente) escribe la Dirección 2 y DESPUÉS publica con la sobrecarga suelta: esa lee siempre de nuevo, así que el
        /// mensaje del alta sale con la Dirección 2 ya escrita aunque el mismo gestor hubiera publicado antes ese cliente.
        /// </summary>
        [TestMethod]
        public async Task LaPublicacionSuelta_LeeLaDireccion2DeNuevo()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.LeerDireccion2Clientes(A<string>._, A<IEnumerable<string>>._))
                .ReturnsNextFromSequence(Filas(("42147", "0", null, null)), Filas(("42147", "0", "LOCAL 2 · RÓTULO: EJEMPLO", "Local")));
            var (gestor, mensajes) = Gestor(servicio);

            await gestor.PublicarClienteSincronizar(Contacto("42147", "0", true), SIN_COMPRAS, "Nesto viejo", null, null);
            await gestor.PublicarClienteSincronizar(Contacto("42147", "0", true));

            Assert.IsNull(mensajes[0].Direccion2);
            Assert.AreEqual("LOCAL 2 · RÓTULO: EJEMPLO", mensajes[1].Direccion2);
        }

        // ---------- Odoo → Nesto: se ignoran ----------

        private static Cliente ClienteNesto() => new Cliente
        {
            Nº_Cliente = "42146     ", Contacto = "0  ", Nombre = "PELUQUERIA EJEMPLO", Teléfono = "916000001", Dirección = "CALLE FALSA, 1",
            Población = "ALGETE", CodPostal = "28110", Provincia = "MADRID", CIF_NIF = "B00000000", Comentarios = "Cierra los lunes", Vendedor = "JE "
        };

        private static ClienteSyncMessage EcoDeOdoo() => new ClienteSyncMessage
        {
            Source = "Odoo", Cliente = "42146", Contacto = "0", Nombre = "PELUQUERIA EJEMPLO", Telefono = "916000001", Direccion = "CALLE FALSA, 1",
            Poblacion = "ALGETE", CodigoPostal = "28110", Provincia = "MADRID", Nif = "B00000000", Comentarios = "Cierra los lunes",
            Direccion2 = "OTRA COSA QUE PUSO ODOO", TipoLugar = "Nave"
        };

        [TestMethod]
        public void DeOdoo_LaDireccion2NoEsUnCambioNiSeAplica()
        {
            Cliente nesto = ClienteNesto();
            ClienteSyncMessage eco = EcoDeOdoo();

            List<string> cambios = new ClienteChangeDetector().DetectarCambios(nesto, eco);
            HashSet<string> campos = BarreraClientesExternos.CamposAAplicar(nesto, eco, new List<CampoDescartadoSincronizacion>());

            Assert.IsFalse(cambios.Any(), string.Join("; ", cambios));
            Assert.AreEqual(0, campos.Count);
        }

        [TestMethod]
        public void DeOdoo_ElMensajeConDireccion2SeLeeSinError()
        {
            string json = "{\"Tabla\":\"Clientes\",\"Source\":\"Odoo\",\"Cliente\":\"42146\",\"Contacto\":\"0\",\"Direccion\":\"CALLE FALSA, 1\"," +
                "\"Direccion2\":\"3º BD\",\"TipoLugar\":\"Piso\"}";

            var mensaje = (ClienteSyncMessage)new ClientesSyncHandler(_ => { }).Deserialize(json, new JsonSerializerOptions());

            Assert.AreEqual("CALLE FALSA, 1", mensaje.Direccion);
            Assert.AreEqual("3º BD", mensaje.Direccion2, "Se lee, pero ni el detector ni la barrera la miran");
        }

        [TestMethod]
        public void ParaPublicar_EsPura()
        {
            var filas = Filas(("42148", "0", "  LOCAL 1 · RÓTULO: X ", "Local "));

            Assert.AreEqual(("LOCAL 1 · RÓTULO: X", "Local"), Direccion2ClienteSql.ParaPublicar(filas, "42148   ", "0 "));
            Assert.AreEqual(((string)null, (string)null), Direccion2ClienteSql.ParaPublicar(filas, "42148", "1"));
            Assert.AreEqual(((string)null, (string)null), Direccion2ClienteSql.ParaPublicar(null, "42148", "0"));
        }
    }
}
