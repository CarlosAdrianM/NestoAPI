using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#739: el reparto propio como agencia (numerar bultos, etiqueta ZPL propia, tramitado en el acto), la propuesta con
    /// el interruptor, el momento del reembolso y los permisos.
    /// </summary>
    [TestClass]
    public class AgenciaRepartoPropioTests
    {
        private const int REPARTO = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;

        private static DatosEnvioRemoto Datos(short bultos = 3, decimal reembolso = 0m) => new DatosEnvioRemoto
        {
            NumeroEnvio = 251300,
            Referencia = "928000",
            Nombre = "PELUQUERÍA ^LA~ BONITA",
            Direccion = "Calle Mayor, 1",
            CodigoPostal = "28110",
            Poblacion = "ALGETE",
            Telefono = "916281914",
            Movil = "600123456",
            Bultos = bultos,
            Reembolso = reembolso,
            FechaRecogida = new DateTime(2026, 10, 29)
        };

        // ================================================================ códigos

        [TestMethod]
        public void Codigos_AlbaranYBulto()
        {
            Assert.AreEqual(14, REPARTO);
            Assert.AreEqual("RP251300", CodigoBultoRepartoPropio.Albaran(251300));
            Assert.AreEqual("RP251300-2", CodigoBultoRepartoPropio.Bulto(251300, 2));
            Assert.IsTrue(CodigoBultoRepartoPropio.TryLeerBulto(" rp251300-12 ", out int envio, out int bulto));
            Assert.AreEqual(251300, envio);
            Assert.AreEqual(12, bulto);
            Assert.IsFalse(CodigoBultoRepartoPropio.TryLeerBulto("RP251300", out _, out _));
            Assert.IsFalse(CodigoBultoRepartoPropio.TryLeerBulto("0082800082909720018884", out _, out _));
            Assert.IsFalse(CodigoBultoRepartoPropio.TryLeerBulto(null, out _, out _));
            Assert.IsTrue(CodigoBultoRepartoPropio.TryLeerAlbaran("RP251300", out int numero));
            Assert.AreEqual(251300, numero);
            Assert.IsFalse(CodigoBultoRepartoPropio.TryLeerAlbaran("RP251300-1", out _));
        }

        // ================================================================ etiqueta

        [TestMethod]
        public void Etiqueta_UnaPorBultoConSuCode128YBultoNDeM()
        {
            string zpl = EtiquetaRepartoPropio.ConstruirZpl(Datos(bultos: 3), new DateTime(2026, 10, 29));

            Assert.AreEqual(3, Regex.Matches(zpl, @"\^XA").Count);
            Assert.AreEqual(3, Regex.Matches(zpl, @"\^XZ").Count);
            for (int i = 1; i <= 3; i++)
            {
                StringAssert.Contains(zpl, "^BCN,200,Y,N,N^FDRP251300-" + i + "^FS", "Code128 del bulto " + i);
                StringAssert.Contains(zpl, "^FDBulto " + i + "/3^FS");
            }
            Assert.AreEqual(3, Regex.Matches(zpl, @"\^GFA,04448,04448,00032,").Count, "El logo en cada etiqueta");
            StringAssert.Contains(zpl, "^CI27");
            StringAssert.Contains(zpl, "Pedido: 928000");
            StringAssert.Contains(zpl, "Fecha: 29/10/2026");
            StringAssert.Contains(zpl, "Calle Mayor, 1");
            StringAssert.Contains(zpl, "28110 ALGETE");
            StringAssert.Contains(zpl, "Tel.: 916281914 / 600123456");
            StringAssert.Contains(zpl, "PELUQUERÍA  LA  BONITA", "^ y ~ fuera: romperían la ZPL");
            Assert.IsFalse(zpl.Contains("REEMBOLSO"), "Sin reembolso no sale");
        }

        [TestMethod]
        public void Etiqueta_ConReembolso_LoLleva_YElCentinelaNo()
        {
            StringAssert.Contains(EtiquetaRepartoPropio.ConstruirZpl(Datos(bultos: 1, reembolso: 1234.5m), DateTime.Today), "REEMBOLSO: 1.234,50 EUR");
            Assert.IsFalse(EtiquetaRepartoPropio.ConstruirZpl(Datos(bultos: 1, reembolso: -1m), DateTime.Today).Contains("REEMBOLSO"));
        }

        [TestMethod]
        public void Etiqueta_DesdeHastaBulto()
        {
            string zpl = EtiquetaRepartoPropio.ConstruirZpl(Datos(bultos: 5), DateTime.Today, 2, 3);
            Assert.AreEqual(2, Regex.Matches(zpl, @"\^XA").Count);
            StringAssert.Contains(zpl, "RP251300-2");
            StringAssert.Contains(zpl, "RP251300-3");
            Assert.IsFalse(zpl.Contains("RP251300-1^"));
        }

        // ================================================================ agencia remota

        [TestMethod]
        public async Task Agencia_Registrar_DaElAlbaranRPLosBultosYLaEtiquetaZpl()
        {
            var agencia = new AgenciaRemotaRepartoPropio(n => Task.FromResult<EnviosAgencia>(null), () => new DateTime(2026, 10, 29));

            ResultadoTramitacionRemota resultado = await agencia.InsertarYEtiquetarAsync(Datos(bultos: 2));

            Assert.IsTrue(resultado.Exito);
            Assert.AreEqual("RP251300", resultado.Albaran);
            Assert.AreEqual(2, resultado.Bultos);
            Assert.IsTrue(resultado.Etiqueta.Exito && resultado.Etiqueta.EsZpl);
            Assert.AreEqual(resultado.Etiqueta.Contenido.Trim(), EnviosAgenciasController.ZplParaImprimir(resultado.Etiqueta), "Va tal cual a la Zebra");
            Assert.AreEqual(0, agencia.Intercambios.Count);
            Assert.IsFalse(agencia.ModificarCreaEnvioNuevo);
        }

        [TestMethod]
        public async Task Agencia_Reimprimir_LeeElEnvioPorSuAlbaran()
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(numero: 251300, bultos: 2);
            var agencia = new AgenciaRemotaRepartoPropio(n => Task.FromResult(n == 251300 ? envio : null));

            EtiquetaDataTrans etiqueta = await agencia.ReimprimirAsync("RP251300");
            EtiquetaDataTrans deOtro = await agencia.ReimprimirAsync("6522393001");

            Assert.IsTrue(etiqueta.Exito && etiqueta.EsZpl);
            StringAssert.Contains(etiqueta.Contenido, "RP251300-2");
            Assert.IsFalse(deOtro.Exito);
            Assert.IsTrue((await agencia.AnularAsync("RP251300")).Exito, "No hay nadie a quien avisar");
            Assert.AreEqual(EstadoEnvioSeguimiento.Desconocido, (await agencia.ConsultarSeguimientoAsync("RP251300")).Estado, "Nunca pisa un estado");
        }

        [TestMethod]
        public void Perfil_ConElInterruptorApagado_NoHayGestionRemota()
        {
            var perfil = new PerfilAgenciaRepartoPropio { Activo = () => false };
            Assert.AreEqual(REPARTO, perfil.AgenciaId);
            Assert.IsNull(perfil.CrearGestionRemota(A.Fake<NVEntities>()));

            perfil.Activo = () => true;
            Assert.IsInstanceOfType(perfil.CrearGestionRemota(A.Fake<NVEntities>()), typeof(AgenciaRemotaRepartoPropio));
            Assert.IsNotNull(RegistroAgencias.PorReflexionSinPuerta().Perfil(REPARTO), "Descubierto por reflexión");
        }

        [TestMethod]
        public void Interruptor_SoloTrueEnciende()
        {
            Assert.IsTrue(InterruptorRepartoPropio.EstaActivo(" TRUE "));
            Assert.IsFalse(InterruptorRepartoPropio.EstaActivo("false"));
            Assert.IsFalse(InterruptorRepartoPropio.EstaActivo(null));
            Assert.IsFalse(InterruptorRepartoPropio.EstaActivo("1"));
        }

        [TestMethod]
        public void Flujo_RegistrarAlImprimir_ComoCttEInnovatrans()
        {
            Assert.AreEqual(FlujosEnvioDesdePedido.REGISTRAR_AL_IMPRIMIR, ServicioEnvioDesdePedido.FlujoDe(REPARTO));
        }

        // ================================================================ momento del reembolso

        [TestMethod]
        public void MomentoReembolso_RepartoPropioAlEntregar_LasDemasAlTramitar()
        {
            Assert.AreEqual(MomentoContabilizarReembolso.AlEntregar, MomentosContabilizarReembolso.De(REPARTO));
            foreach (int agencia in new[] { 1, 5, 7, 8, 10, 11, 12, 13 })
            {
                Assert.AreEqual(MomentoContabilizarReembolso.AlTramitar, MomentosContabilizarReembolso.De(agencia), "Agencia " + agencia);
            }
        }

        [TestMethod]
        public void MomentoReembolso_TramitarNoContabilizaElDelRepartoPropio()
        {
            Assert.IsFalse(TramitacionEnviosService.ContabilizaReembolsoAlTramitar(new EnviosAgencia { Agencia = REPARTO, Reembolso = 85.40m }),
                "Se contabiliza al entregar, contra la caja del repartidor");
            Assert.IsTrue(TramitacionEnviosService.ContabilizaReembolsoAlTramitar(new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_INNOVATRANS, Reembolso = 85.40m }),
                "Las de fuera, como siempre");
            Assert.IsFalse(TramitacionEnviosService.ContabilizaReembolsoAlTramitar(new EnviosAgencia { Agencia = Constantes.Agencias.AGENCIA_INNOVATRANS, Reembolso = 0m }));
        }

        // ================================================================ Tramitar: queda Tramitado en el acto

        [TestMethod]
        public async Task Tramitar_RepartoPropio_NumeraLosBultosYCierraElEnvio()
        {
            NVEntities db = A.Fake<NVEntities>();
            var fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            var fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            var envio = new EnviosAgencia
            {
                Numero = 251300, Agencia = REPARTO, Pedido = 928000, Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE,
                Nombre = "CLIENTE", Direccion = "Calle Mayor 1", CodPostal = "28110", Poblacion = "ALGETE", Bultos = 2, Reembolso = 85.40m
            };
            A.CallTo(() => fakeEnvios.FindAsync(251300)).Returns(Task.FromResult(envio));
            var fabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fabrica.Crear(REPARTO)).Returns(new AgenciaRemotaRepartoPropio(n => Task.FromResult(envio)));
            var tramitacion = A.Fake<ITramitacionEnviosService>();
            A.CallTo(() => tramitacion.TramitarAsync(251300, A<string>._)).Invokes(() => envio.Estado = Constantes.Agencias.ESTADO_TRAMITADO)
                .Returns(Task.FromResult(new ResultadoTramitacionEnvio { Numero = 251300 }));
            var controller = new EnviosAgenciasController(db, fabrica)
            {
                MotivoPedidoRetenido = (e, p) => Task.FromResult<string>(null),
                TramitacionEnvios = tramitacion,
                Request = new HttpRequestMessage()
            };

            var resultado = await controller.TramitarEnvio(251300) as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("RP251300", envio.CodigoBarras);
            Assert.AreEqual("RP251300", resultado.Content.Albaran);
            Assert.AreEqual(2, resultado.Content.Bultos);
            StringAssert.Contains(resultado.Content.EtiquetaContenido, "RP251300-2");
            Assert.AreEqual(0m + 85.40m, envio.Reembolso);
            A.CallTo(() => tramitacion.TramitarAsync(251300, A<string>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado, "Sin cierre del día: Tramitado en el acto");

            // Repetir = reimprimir, sin volver a cerrar (ya está tramitado).
            var otraVez = await controller.TramitarEnvio(251300) as OkNegotiatedContentResult<TramitarEnvioResultadoDTO>;
            Assert.IsTrue(otraVez.Content.Reimpresion);
            A.CallTo(() => tramitacion.TramitarAsync(251300, A<string>._)).MustHaveHappenedOnceExactly();
        }

        // ================================================================ propuesta

        private static PropuestaEnvioService Propuesta(string ruta, bool interruptor, bool conAgencia14)
        {
            IDatosPropuestaEnvio datos = A.Fake<IDatosPropuestaEnvio>();
            A.CallTo(() => datos.LeerPedido("1", 928000)).Returns(Task.FromResult(new PedidoParaAgenciaDTO
            {
                Empresa = "1  ", Numero = 928000, Cliente = "15191", Contacto = "0", Vendedor = "JE", Ruta = ruta,
                ClienteFicha = new ClienteParaAgenciaDTO { Nombre = "CLIENTE", Direccion = "Calle Mayor 1", CodPostal = "28110", Poblacion = "ALGETE", Telefono = "916281914", PersonasContacto = new List<PersonaContactoAgenciaDTO>() }
            }));
            A.CallTo(() => datos.TieneAlgunaLineaConPicking(A<string>._, A<int>._)).Returns(Task.FromResult(true));
            A.CallTo(() => datos.LeerEnvioPendiente(A<string>._, A<int>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => datos.LeerEnvioAmpliacion(A<string>._, A<string>._, A<string>._, A<int?>._)).Returns(Task.FromResult<EnviosAgencia>(null));
            A.CallTo(() => datos.BultosPacking(A<string>._, A<int>._)).Returns(Task.FromResult(0));
            A.CallTo(() => datos.FechaPicking(A<string>._)).Returns(Task.FromResult<DateTime?>(new DateTime(2026, 10, 29)));
            var agencias = new List<AgenciaTransporte> { new AgenciaTransporte { Numero = 1, Empresa = "1  ", Nombre = "ASM" } };
            if (conAgencia14)
            {
                agencias.Add(new AgenciaTransporte { Numero = REPARTO, Empresa = "1  ", Nombre = "Reparto propio" });
            }
            A.CallTo(() => datos.LeerAgencias()).Returns(Task.FromResult(agencias));
            var registro = A.Fake<IRegistroTarifas>();
            A.CallTo(() => registro.Todas()).Returns(new ITarifaAgencia[] { new TarifaGLSBusinessParcel() });
            var fuel = A.Fake<IProveedorRecargoCombustible>();
            A.CallTo(() => datos.Comparador()).ReturnsLazily(() => new ComparadorAgencias(registro, fuel));
            return new PropuestaEnvioService(datos) { RepartoPropioActivo = () => interruptor };
        }

        [TestMethod]
        public async Task Propuesta_RutaPropiaConInterruptor_RepartoPropio()
        {
            foreach (string ruta in new[] { "AT ", "16 ", "at" })
            {
                PropuestaEnvioDTO p = await Propuesta(ruta, true, true).Calcular("1", 928000, null, null);
                Assert.AreEqual(REPARTO, p.Agencia, "Ruta " + ruta);
                Assert.AreEqual("Reparto propio", p.AgenciaNombre);
                Assert.AreEqual("1", p.Empresa);
            }
        }

        [TestMethod]
        public async Task Propuesta_InterruptorApagadoOtraRutaOSinLaFila14_LaDeSiempre()
        {
            Assert.AreEqual(Constantes.Agencias.AGENCIA_GLS, (await Propuesta("AT ", false, true).Calcular("1", 928000, null, null)).Agencia, "Apagado");
            Assert.AreEqual(Constantes.Agencias.AGENCIA_GLS, (await Propuesta("10 ", true, true).Calcular("1", 928000, null, null)).Agencia, "Otra ruta");
            Assert.AreEqual(Constantes.Agencias.AGENCIA_GLS, (await Propuesta(null, true, true).Calcular("1", 928000, null, null)).Agencia, "Sin ruta");
            Assert.AreEqual(Constantes.Agencias.AGENCIA_GLS, (await Propuesta("AT ", true, false).Calcular("1", 928000, null, null)).Agencia, "Sin el script");
        }

        // NestoAPI#750 (Nesto#576): Nesto lee de la propuesta la agencia, el servicio y el horario. Del reparto propio todo es 0
        // (un solo servicio, sin horarios ni retornos) y sin coste de agencia, que es lo que grabará Nesto.
        [TestMethod]
        public async Task Propuesta_RutaPropia_ServicioHorarioRetornoYCosteACero()
        {
            PropuestaEnvioDTO p = await Propuesta("AT ", true, true).Calcular("1", 928000, null, 3.5m);

            Assert.AreEqual(REPARTO, p.Agencia);
            Assert.AreEqual(PropuestaEnvioDTO.ORIGEN_NUEVO, p.Origen);
            Assert.AreEqual(0, p.Servicio);
            Assert.AreEqual(0, p.Horario);
            Assert.AreEqual(0, p.Retorno);
            Assert.AreEqual(0m, p.ImporteGasto);
            Assert.AreEqual("28110", p.CodPostal, "Nesto solo la aplica si el CP de la pantalla es este");
            Assert.AreEqual("ES", p.PaisIso);
        }

        // NestoAPI#750 (Nesto#576): la sombra de #595. Con lo que grababa Nesto (ELMAH 30/10/26, envío 250375, pedido 929440)
        // salían 3 diferencias; con lo que graba ahora (lo propuesto), ninguna.
        [TestMethod]
        public async Task Sombra_NestoGrabaLaPropuestaDelRepartoPropio_SinDiferencias()
        {
            PropuestaEnvioDTO p = await Propuesta("16 ", true, true).Calcular("1", 928000, 1, 3.5m);
            EnviosAgencia envio = new EnviosAgencia
            {
                Numero = 250375, Pedido = p.Pedido, Agencia = REPARTO, Servicio = 0, Horario = 0, Retorno = 0,
                Reembolso = p.Reembolso, Nombre = p.Nombre, Direccion = p.Direccion, CodPostal = p.CodPostal, Poblacion = p.Poblacion,
                Provincia = p.Provincia, Pais = p.Pais, Telefono = p.Telefono, Movil = p.Movil, Email = p.Email, Atencion = p.Atencion,
                Vendedor = p.Vendedor, Bultos = p.Bultos, Peso = p.Peso, ImporteGasto = 0m, Empresa = "1  ",
                Fecha = p.Fecha, FechaEntrega = p.FechaEntrega, Observaciones = p.Observaciones
            };

            CollectionAssert.AreEqual(new List<string>(), ComparadorPropuestaEnvio.Diferencias(envio, p));

            envio.Agencia = Constantes.Agencias.AGENCIA_CTT;
            envio.Servicio = 48;
            envio.ImporteGasto = 3.896450m;
            List<string> antes = ComparadorPropuestaEnvio.Diferencias(envio, p);
            CollectionAssert.AreEqual(new List<string>
            {
                "agencia: Nesto=13 / propuesta=14",
                "servicio: Nesto=48 / propuesta=0",
                "ImporteGasto: Nesto=3.896450 / propuesta=0"
            }, antes, string.Join(" | ", antes));
        }

        // ================================================================ permisos

        private static ClaimsPrincipal Usuario(params string[] grupos)
            => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\alguien") }
                .Concat(grupos.Select(g => new Claim(ClaimTypes.Role, @"NUEVAVISION\" + g))), "Bearer"));

        [TestMethod]
        public void Permisos_RepartoYDatosPersonales()
        {
            foreach (string grupo in new[] { "Almacén", "Administración", "Dirección", "Informática" })
            {
                Assert.IsTrue(PermisosRepartoPropio.PuedeRepartir(Usuario(grupo)), grupo);
            }
            Assert.IsFalse(PermisosRepartoPropio.PuedeRepartir(Usuario("Tiendas")));
            Assert.IsFalse(PermisosRepartoPropio.PuedeRepartir(Usuario()));
            Assert.IsFalse(PermisosRepartoPropio.PuedeRepartir(null));

            foreach (string grupo in new[] { "Almacén", "Administración", "Dirección" })
            {
                Assert.IsTrue(PermisosRepartoPropio.PuedeVerDatosPersonales(Usuario(grupo)), grupo);
            }
            Assert.IsFalse(PermisosRepartoPropio.PuedeVerDatosPersonales(Usuario("Informática")), "Firma y DNI: datos personales");
            Assert.IsFalse(PermisosRepartoPropio.PuedeVerDatosPersonales(Usuario("Tiendas")));
        }

        private static async Task<HttpStatusCode> Estado(IHttpActionResult resultado)
            => (await resultado.ExecuteAsync(CancellationToken.None)).StatusCode;

        private static RepartoPropioController Controlador(ClaimsPrincipal usuario, ServicioRepartoPropioTests.RepositorioFalso repositorio)
            => new RepartoPropioController(A.Fake<NVEntities>(), new ServicioRepartoPropio(repositorio, () => new DateTime(2026, 10, 29, 12, 0, 0)))
            {
                User = usuario,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };

        [TestMethod]
        public async Task Controlador_SinGrupo403_ConAlmacenResponde()
        {
            var repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            repositorio.Con(ServicioRepartoPropioTests.Envio());

            RepartoPropioController sinPermiso = Controlador(Usuario("Tiendas"), repositorio);
            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(await sinPermiso.GetEnvios()));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(await sinPermiso.GetMisCobros()));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(await sinPermiso.Entregar(251300, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() })));
            Assert.AreEqual(0, repositorio.Registros.Count);

            RepartoPropioController informatica = Controlador(Usuario("Informática"), repositorio);
            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(await informatica.GetPruebaEntrega(251300)));
            Assert.AreEqual(HttpStatusCode.Forbidden, await Estado(await informatica.GetFirma(251300)));

            RepartoPropioController almacen = Controlador(Usuario("Almacén"), repositorio);
            Assert.AreEqual(HttpStatusCode.OK, await Estado(await almacen.GetBulto("RP251300-1")));
            Assert.AreEqual(HttpStatusCode.NotFound, await Estado(await almacen.GetBulto("RP1-1")));
            Assert.AreEqual(HttpStatusCode.NoContent, await Estado(await almacen.GetPruebaEntrega(251300)));
            Assert.AreEqual(HttpStatusCode.BadRequest, await Estado(await almacen.Entregar(251300, new EntregaRepartoDTO())));
            Assert.AreEqual(HttpStatusCode.OK, await Estado(await almacen.Entregar(251300, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() })));
            Assert.AreEqual(HttpStatusCode.Conflict, await Estado(await almacen.Incidencia(251300, new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = "Ausente" })));
        }

        [TestMethod]
        public void Controlador_LlevaAuthorize()
        {
            Assert.IsTrue(typeof(RepartoPropioController).GetCustomAttributes(typeof(AuthorizeAttribute), false).Any());
        }
    }
}
