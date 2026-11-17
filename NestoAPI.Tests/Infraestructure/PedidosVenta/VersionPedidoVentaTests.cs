using FakeItEasy;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#873 (pedido 929555, 16/11/26): Aida guardó el pedido con un prepago a las 15:14 y Alfredo, con el pedido abierto
    /// desde antes, lo guardó a las 15:17 con «Prepagos: []»: el PUT borró el prepago. Versión del pedido + 409 + prepagos
    /// protegidos + aviso «pedidoCambiado».
    /// </summary>
    [TestClass]
    public class VersionPedidoVentaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 16);

        private static DatosVersionPedido Datos()
        {
            return new DatosVersionPedido
            {
                RowVersionCabecera = new byte[] { 0, 0, 0, 0, 0, 1, 2, 3 },
                UsuarioCabecera = "NUEVAVISION\\Alfredo",
                FechaCabecera = new DateTime(2026, 11, 16, 9, 0, 0),
                Lineas = 3,
                RowVersionLineas = new byte[] { 0, 0, 0, 0, 0, 1, 2, 9 },
                UsuarioLinea = "NUEVAVISION\\Alfredo",
                FechaLinea = new DateTime(2026, 11, 16, 9, 0, 0),
                Prepagos = 0,
                Efectos = 0
            };
        }

        #region La regla pura

        [TestMethod]
        public void Calcular_LosMismosDatos_LaMismaVersion()
        {
            Assert.AreEqual(VersionPedidoVenta.Calcular(Datos()), VersionPedidoVenta.Calcular(Datos()));
            Assert.AreEqual(32, VersionPedidoVenta.Calcular(Datos()).Length, "Opaca: 32 caracteres hexadecimales");
        }

        [TestMethod]
        public void Calcular_SinDatos_Null()
        {
            Assert.IsNull(VersionPedidoVenta.Calcular(null));
        }

        [TestMethod]
        public void Calcular_CambiaConLaCabeceraLasLineasLosPrepagosYLosEfectos()
        {
            string original = VersionPedidoVenta.Calcular(Datos());
            var cambios = new List<Action<DatosVersionPedido>>
            {
                d => d.RowVersionCabecera = new byte[] { 0, 0, 0, 0, 0, 1, 2, 4 },
                d => d.Lineas = 2,
                d => d.RowVersionLineas = new byte[] { 0, 0, 0, 0, 0, 1, 3, 0 },
                d => { d.Prepagos = 1; d.SumaIdPrepagos = 117657; d.HuellaPrepagos = 12345; },
                d => { d.Efectos = 2; d.SumaIdEfectos = 30; d.HuellaEfectos = -77; }
            };
            foreach (Action<DatosVersionPedido> cambio in cambios)
            {
                DatosVersionPedido datos = Datos();
                cambio(datos);
                Assert.AreNotEqual(original, VersionPedidoVenta.Calcular(datos));
            }
        }

        [TestMethod]
        public void Calcular_ElPrepagoDel929555CambiaLaVersion()
        {
            DatosVersionPedido sinPrepago = Datos();
            DatosVersionPedido conPrepago = Datos();
            conPrepago.Prepagos = 1;
            conPrepago.SumaIdPrepagos = 117657;
            conPrepago.HuellaPrepagos = 987654;

            Assert.AreNotEqual(VersionPedidoVenta.Calcular(sinPrepago), VersionPedidoVenta.Calcular(conPrepago));
        }

        [TestMethod]
        public void Calcular_QuienYCuandoNoCuentan()
        {
            DatosVersionPedido otro = Datos();
            otro.UsuarioCabecera = "Otro";
            otro.FechaLinea = HOY;

            Assert.AreEqual(VersionPedidoVenta.Calcular(Datos()), VersionPedidoVenta.Calcular(otro));
        }

        [TestMethod]
        public void HayConflicto_SinVersionDelCliente_NoHay()
        {
            Assert.IsFalse(VersionPedidoVenta.HayConflicto(null, "abc"));
            Assert.IsFalse(VersionPedidoVenta.HayConflicto("  ", "abc"));
        }

        [TestMethod]
        public void HayConflicto_SinVersionActual_NoHay()
        {
            Assert.IsFalse(VersionPedidoVenta.HayConflicto("abc", null));
        }

        [TestMethod]
        public void HayConflicto_LaMisma_NoHay_Distinta_Si()
        {
            Assert.IsFalse(VersionPedidoVenta.HayConflicto("ABC", "abc "));
            Assert.IsTrue(VersionPedidoVenta.HayConflicto("abc", "abd"));
        }

        [TestMethod]
        public void PuedeQuitarPrepagosQueNoVienen_SoloConLaVersionComprobada()
        {
            Assert.IsTrue(VersionPedidoVenta.PuedeQuitarPrepagosQueNoVienen(true));
            Assert.IsFalse(VersionPedidoVenta.PuedeQuitarPrepagosQueNoVienen(false));
        }

        [TestMethod]
        public void UltimaModificacion_LaMasRecienteYSinDominio()
        {
            DatosVersionPedido datos = Datos();
            datos.UsuarioPrepago = "NUEVAVISION\\Aida";
            datos.FechaPrepago = new DateTime(2026, 11, 16, 15, 14, 0);

            UltimaModificacionPedido ultima = VersionPedidoVenta.UltimaModificacion(datos);

            Assert.AreEqual("Aida", ultima.Usuario);
            Assert.AreEqual(new DateTime(2026, 11, 16, 15, 14, 0), ultima.Fecha);
        }

        [TestMethod]
        public void MensajeConflicto_ElDeLaIssue()
        {
            var ultima = new UltimaModificacionPedido { Usuario = "Aida", Fecha = new DateTime(2026, 11, 16, 15, 14, 0) };

            string mensaje = VersionPedidoVenta.MensajeConflicto(929555, ultima, "NUEVAVISION\\Alfredo", HOY);

            Assert.AreEqual("El pedido 929555 lo ha modificado Aida a las 15:14 desde que usted lo abrió. Recárguelo y vuelva a hacer los cambios.", mensaje);
        }

        [TestMethod]
        public void MensajeConflicto_OtroDia_ConLaFecha()
        {
            var ultima = new UltimaModificacionPedido { Usuario = "Aida", Fecha = new DateTime(2026, 11, 15, 8, 5, 0) };

            string mensaje = VersionPedidoVenta.MensajeConflicto(929555, ultima, "Alfredo", HOY);

            Assert.AreEqual("El pedido 929555 lo ha modificado Aida el 15/11/2026 a las 08:05 desde que usted lo abrió. Recárguelo y vuelva a hacer los cambios.", mensaje);
        }

        [TestMethod]
        public void MensajeConflicto_ElMismoUsuario_DiceQueHaSidoUstedMismo()
        {
            var ultima = new UltimaModificacionPedido { Usuario = "Alfredo", Fecha = new DateTime(2026, 11, 16, 15, 14, 0) };

            string mensaje = VersionPedidoVenta.MensajeConflicto(929555, ultima, "NUEVAVISION\\Alfredo", HOY);

            StringAssert.StartsWith(mensaje, "El pedido 929555 lo ha modificado usted mismo a las 15:14");
            StringAssert.EndsWith(mensaje, "Recárguelo y vuelva a hacer los cambios.");
        }

        [TestMethod]
        public void MensajeConflicto_SinSaberQuien()
        {
            Assert.AreEqual("El pedido 929555 ha cambiado desde que usted lo abrió. Recárguelo y vuelva a hacer los cambios.",
                VersionPedidoVenta.MensajeConflicto(929555, null, "Alfredo", HOY));
        }

        [TestMethod]
        public void MensajePrepagoConservado_ConImporteYConcepto()
        {
            string mensaje = VersionPedidoVenta.MensajePrepagoConservado(9728.47M, "por TRN pedido 929555 ");

            StringAssert.StartsWith(mensaje, "No se ha quitado el prepago de 9.728,47 € («por TRN pedido 929555»)");
        }

        #endregion

        #region PUT: 409

        private static DbSet<T> CrearDbSet<T>(IEnumerable<T> datos) where T : class
        {
            IQueryable<T> consulta = datos.AsQueryable();
            DbSet<T> dbSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)dbSet).Provider).Returns(consulta.Provider);
            A.CallTo(() => ((IQueryable<T>)dbSet).Expression).Returns(consulta.Expression);
            A.CallTo(() => ((IQueryable<T>)dbSet).ElementType).Returns(consulta.ElementType);
            A.CallTo(() => ((IQueryable<T>)dbSet).GetEnumerator()).ReturnsLazily(() => consulta.GetEnumerator());
            return dbSet;
        }

        private static (PedidosVentaController Controller, NVEntities Db, ILectorVersionPedido Lector) ControllerConPedido929555(DatosVersionPedido datosActuales)
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<CabPedidoVta> cabeceras = CrearDbSet(new List<CabPedidoVta> { new CabPedidoVta { Empresa = "1", Número = 929555 } });
            A.CallTo(() => db.CabPedidoVtas).Returns(cabeceras);
            ILectorVersionPedido lector = A.Fake<ILectorVersionPedido>();
            A.CallTo(() => lector.Leer("1", 929555)).Returns(datosActuales);
            var controller = new PedidosVentaController(db) { LectorVersionPedido = lector };
            return (controller, db, lector);
        }

        [TestMethod]
        public async Task PutPedidoVenta_ConUnaVersionVieja_409ConElMensajeYSinTocarNada()
        {
            // Lo que había cuando Alfredo abrió el pedido (sin el prepago)...
            string versionDeAlfredo = VersionPedidoVenta.Calcular(Datos());
            // ...y lo que hay ahora: Aida le ha metido el prepago a las 15:14
            DatosVersionPedido ahora = Datos();
            ahora.Prepagos = 1;
            ahora.SumaIdPrepagos = 117657;
            ahora.HuellaPrepagos = 987654;
            ahora.UsuarioPrepago = "NUEVAVISION\\Aida";
            ahora.FechaPrepago = DateTime.Today.AddHours(15).AddMinutes(14);
            var (controller, db, _) = ControllerConPedido929555(ahora);
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 929555, cliente = "15191", contacto = "0", Usuario = "NUEVAVISION\\Alfredo", Version = versionDeAlfredo };

            IHttpActionResult resultado = await controller.PutPedidoVenta(pedido);

            var conflicto = resultado as NegotiatedContentResult<object>;
            Assert.IsNotNull(conflicto, $"Tenía que ser un 409 y ha sido {resultado?.GetType().Name}");
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            JObject cuerpo = JObject.FromObject(conflicto.Content);
            Assert.AreEqual(VersionPedidoVenta.CODIGO_CONFLICTO, (string)cuerpo["error"]["code"]);
            Assert.AreEqual("El pedido 929555 lo ha modificado Aida a las 15:14 desde que usted lo abrió. Recárguelo y vuelva a hacer los cambios.",
                (string)cuerpo["Message"]);
            Assert.AreEqual(VersionPedidoVenta.Calcular(ahora), (string)cuerpo["error"]["details"]["Version"]);
            A.CallTo(() => db.Modificaciones).MustNotHaveHappened();
            A.CallTo(() => db.Prepagos).MustNotHaveHappened();
            A.CallTo(() => db.SaveChanges()).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutPedidoVenta_SinVersion_NoLaMiraYSigueComoAntes()
        {
            // NestoApp, TiendasNuevaVision y Nesto viejo no la mandan: ni 409 ni consulta de la versión antes de guardar
            var (controller, _, lector) = ControllerConPedido929555(Datos());
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 929555, cliente = "15191", contacto = "0", Usuario = "Alfredo" };

            object resultado = await SinExcepciones(() => controller.PutPedidoVenta(pedido));

            Assert.IsFalse(resultado is NegotiatedContentResult<object> n && n.StatusCode == HttpStatusCode.Conflict);
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutPedidoVenta_ConLaVersionBuena_NoDa409()
        {
            var (controller, _, lector) = ControllerConPedido929555(Datos());
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 929555, cliente = "15191", contacto = "0", Usuario = "Alfredo", Version = VersionPedidoVenta.Calcular(Datos()) };

            object resultado = await SinExcepciones(() => controller.PutPedidoVenta(pedido));

            Assert.IsFalse(resultado is NegotiatedContentResult<object> n && n.StatusCode == HttpStatusCode.Conflict);
            A.CallTo(() => lector.Leer("1", 929555)).MustHaveHappened();
        }

        [TestMethod]
        public async Task PutPedidoVenta_SiNoSePuedeLeerLaVersion_NoDa409()
        {
            var (controller, _, lector) = ControllerConPedido929555(null);
            A.CallTo(() => lector.Leer(A<string>._, A<int>._)).Throws(new InvalidOperationException("caída"));
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 929555, cliente = "15191", contacto = "0", Usuario = "Alfredo", Version = "vieja" };

            object resultado = await SinExcepciones(() => controller.PutPedidoVenta(pedido));

            Assert.IsFalse(resultado is NegotiatedContentResult<object> n && n.StatusCode == HttpStatusCode.Conflict);
        }

        private static async Task<object> SinExcepciones(Func<Task<IHttpActionResult>> accion)
        {
            try
            {
                return await accion();
            }
            catch (Exception ex)
            {
                // Con la BD falsa el PUT acaba fallando más adelante: lo que importa es lo que pasa antes
                return ex;
            }
        }

        #endregion

        #region Prepagos (regresión del 929555)

        private static (PedidosVentaController Controller, NVEntities Db, CabPedidoVta Cabecera, Prepago PrepagoDeAida) PedidoConElPrepagoDeAida()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<Prepago> prepagos = A.Fake<DbSet<Prepago>>();
            A.CallTo(() => db.Prepagos).Returns(prepagos);
            var prepagoDeAida = new Prepago { Id = 117657, Empresa = "1", Pedido = 929555, Importe = 9728.47M, CuentaContable = "57200001", ConceptoAdicional = "por TRN pedido 929555" };
            var facturado = new Prepago { Id = 1, Empresa = "1", Pedido = 929555, Importe = 10M, Factura = "NV26/00001", ConceptoAdicional = "viejo" };
            var cabecera = new CabPedidoVta { Empresa = "1", Número = 929555 };
            cabecera.Prepagos.Add(prepagoDeAida);
            cabecera.Prepagos.Add(facturado);
            return (new PedidosVentaController(db), db, cabecera, prepagoDeAida);
        }

        [TestMethod]
        public void AplicarPrepagos_Pedido929555_GuardadoSinVersionYSinPrepagos_NoBorraElPrepagoYAvisa()
        {
            var (controller, db, cabecera, prepagoDeAida) = PedidoConElPrepagoDeAida();
            var pedidoDeAlfredo = new PedidoVentaDTO { empresa = "1", numero = 929555, Usuario = "NUEVAVISION\\Alfredo" }; // Prepagos: []

            List<AvisoPedidoDTO> avisos = controller.AplicarPrepagosAlModificar(cabecera, pedidoDeAlfredo, versionComprobada: false, new FechasCobroPrepagosAGuardar());

            A.CallTo(() => db.Prepagos.Remove(A<Prepago>._)).MustNotHaveHappened();
            Assert.IsTrue(cabecera.Prepagos.Contains(prepagoDeAida));
            Assert.AreEqual(1, avisos.Count);
            Assert.AreEqual(VersionPedidoVenta.TIPO_AVISO_PREPAGO_CONSERVADO, avisos[0].Tipo);
            StringAssert.Contains(avisos[0].Mensaje, "9.728,47 €");
        }

        [TestMethod]
        public void AplicarPrepagos_ConLaVersionComprobadaYSinPrepagos_SeQuitanLosSinFactura()
        {
            var (controller, db, cabecera, prepagoDeAida) = PedidoConElPrepagoDeAida();
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 929555, Usuario = "Aida" };

            List<AvisoPedidoDTO> avisos = controller.AplicarPrepagosAlModificar(cabecera, pedido, versionComprobada: true, new FechasCobroPrepagosAGuardar());

            A.CallTo(() => db.Prepagos.Remove(prepagoDeAida)).MustHaveHappenedOnceExactly();
            A.CallTo(() => db.Prepagos.Remove(A<Prepago>.That.Matches(p => p.Factura != null))).MustNotHaveHappened();
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void AplicarPrepagos_SinVersionConUnPrepagoNuevo_SeAnadeComoSiempre()
        {
            var (controller, db, cabecera, prepagoDeAida) = PedidoConElPrepagoDeAida();
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 929555, Usuario = "Aida" };
            pedido.Prepagos.Add(new PrepagoDTO { Importe = 50M, CuentaContable = "57200001", ConceptoAdicional = "otro" });

            List<AvisoPedidoDTO> avisos = controller.AplicarPrepagosAlModificar(cabecera, pedido, versionComprobada: false, new FechasCobroPrepagosAGuardar());

            Assert.AreEqual(3, cabecera.Prepagos.Count);
            Assert.IsTrue(cabecera.Prepagos.Contains(prepagoDeAida), "Los que no trae no se tocan, como siempre");
            A.CallTo(() => db.Prepagos.Remove(A<Prepago>._)).MustNotHaveHappened();
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public void AplicarPrepagos_ElMismoConcepto_ActualizaElImporte()
        {
            var (controller, _, cabecera, prepagoDeAida) = PedidoConElPrepagoDeAida();
            var pedido = new PedidoVentaDTO { empresa = "1", numero = 929555, Usuario = "Aida" };
            pedido.Prepagos.Add(new PrepagoDTO { Importe = 9700M, CuentaContable = "57200001", ConceptoAdicional = "por TRN pedido 929555" });

            _ = controller.AplicarPrepagosAlModificar(cabecera, pedido, versionComprobada: false, new FechasCobroPrepagosAGuardar());

            Assert.AreEqual(9700M, prepagoDeAida.Importe);
            Assert.AreEqual(2, cabecera.Prepagos.Count);
        }

        #endregion

        #region SignalR

        [TestMethod]
        public void GrupoPedido_YNormalizar()
        {
            Assert.AreEqual("Pedido:1:929555", AvisosAlmacen.GrupoPedido(" 1 ", 929555));
            Assert.AreEqual("Pedido:1:929555", AvisosAlmacen.Normalizar("pedido:1:929555"));
            Assert.IsNull(AvisosAlmacen.Normalizar("Pedido:1:abc"));
            Assert.AreEqual("Recogida:PICK:99633", AvisosAlmacen.Normalizar("Recogida:PICK:99633"), "Los demás grupos de 3 partes siguen igual");
        }

        [TestMethod]
        public void PedidoCambiado_ConAlguienMirandoElPedido_LoMandaAlGrupoConSusDatos()
        {
            ConexionesAlmacen.Vaciar();
            try
            {
                ConexionesAlmacen.Entrar("c1", "Alfredo", "RDS2016", "Pedido:1:929555");
                var recibidos = new List<PedidoCambiadoDTO>();
                var grupos = new List<string>();
                dynamic grupo = new ExpandoObject();
                grupo.pedidoCambiado = new Action<PedidoCambiadoDTO>(a => recibidos.Add(a));
                IHubContext contexto = A.Fake<IHubContext>();
                IHubConnectionContext<dynamic> clientes = A.Fake<IHubConnectionContext<dynamic>>();
                A.CallTo(() => contexto.Clients).Returns(clientes);
                A.CallTo(() => clientes.Group(A<string>._, A<string[]>._)).ReturnsLazily((string nombre, string[] fuera) => { grupos.Add(nombre); return (object)grupo; });
                var avisos = new AvisosAlmacenSignalR(() => contexto);
                var aviso = new PedidoCambiadoDTO { Empresa = "1", Pedido = 929555, Usuario = "Aida", Fecha = HOY, Version = "abc" };

                avisos.PedidoCambiado(aviso);

                CollectionAssert.AreEqual(new[] { "Pedido:1:929555" }, grupos);
                Assert.AreSame(aviso, recibidos.Single());
            }
            finally
            {
                ConexionesAlmacen.Vaciar();
            }
        }

        [TestMethod]
        public void PedidoCambiado_SinNadieMirando_NoMandaNada()
        {
            ConexionesAlmacen.Vaciar();
            IHubContext contexto = A.Fake<IHubContext>();
            var avisos = new AvisosAlmacenSignalR(() => contexto);

            avisos.PedidoCambiado(new PedidoCambiadoDTO { Empresa = "1", Pedido = 929555 });

            A.CallTo(() => contexto.Clients).MustNotHaveHappened();
        }

        [TestMethod]
        public void PedidoCambiado_SiFallaElHub_NoLanza()
        {
            ConexionesAlmacen.Vaciar();
            try
            {
                ConexionesAlmacen.Entrar("c1", "Alfredo", "RDS2016", "Pedido:1:929555");
                var avisos = new AvisosAlmacenSignalR(() => throw new InvalidOperationException("sin hub"));

                avisos.PedidoCambiado(new PedidoCambiadoDTO { Empresa = "1", Pedido = 929555 });
            }
            finally
            {
                ConexionesAlmacen.Vaciar();
            }
        }

        #endregion
    }
}
