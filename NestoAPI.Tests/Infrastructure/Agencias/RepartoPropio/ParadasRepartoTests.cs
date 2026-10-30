using FakeItEasy;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#759 (corte 3 de #755, ajustes de Carlos del 30/10/26): envíos, notas y reposiciones como paradas. Cada parada es
    /// del repartidor que la carga; «mis paradas» = las suyas + las de nadie; un bulto de la parada de otro, 409 claro; NE/NR al
    /// cargar; el orden por repartidor (último gana); recoger con bultos y «No había nada que recoger»; las reposiciones a una
    /// tienda, una parada por tienda y día; y el aviso «repartoCambiado» por SignalR (sin temporizadores).
    /// </summary>
    [TestClass]
    public class ParadasRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 30, 8, 0, 0);
        private const string ANDRE = "Andre";
        private const string MIGUEL = @"NUEVAVISION\Miguel";
        private const int NOTA_ENTREGA = 929667;
        private const int NOTA_RECOGIDA = 929554;

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private CargaFurgonetaTests.CargaFalsa cargas;
        private NotasFalsas notas;
        private OrdenFalso orden;
        private ReposicionesFalsas reposiciones;
        private BultosReposicionFalsos etiquetas;
        private AvisosFalsos avisos;
        private IServicioParadasNotaEntrega resolutor;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Preparar()
        {
            ConexionesAlmacen.Vaciar();
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            cargas = new CargaFurgonetaTests.CargaFalsa();
            notas = new NotasFalsas();
            orden = new OrdenFalso(repositorio);
            reposiciones = new ReposicionesFalsas();
            etiquetas = new BultosReposicionFalsos();
            avisos = new AvisosFalsos();
            resolutor = A.Fake<IServicioParadasNotaEntrega>();
            A.CallTo(() => resolutor.Resolver(A<string>._)).ReturnsLazily((string codigo) => Task.FromResult(Resolver(codigo)));
            servicio = new ServicioRepartoPropio(repositorio, () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), cargas, new DependenciasParadasReparto
                {
                    Notas = resolutor,
                    NotasParada = notas,
                    Orden = orden,
                    Reposiciones = reposiciones,
                    BultosReposicion = etiquetas,
                    Avisos = avisos
                });
        }

        [TestCleanup]
        public void Limpiar() => ConexionesAlmacen.Vaciar();

        // ---------------------------------------------------------------- datos

        private EnviosAgencia Envio(int numero, int? pedido = 928000, short estado = Constantes.Agencias.ESTADO_TRAMITADO, short bultos = 2, DateTime? fecha = null)
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(numero, estado, bultos: bultos);
            envio.Pedido = pedido;
            envio.Fecha = fecha ?? AHORA.Date;
            return repositorio.Con(envio);
        }

        /// <summary>Las dos notas de la épica: 929667 (entrega, envío 251500) y 929554 (recogida de 2 bultos, envío 251501).</summary>
        private void ConNotas()
        {
            Envio(251500, NOTA_ENTREGA, bultos: 1);
            Envio(251501, NOTA_RECOGIDA, bultos: 2);
            notas.Filas.Add(new NotaParadaReparto { Pedido = NOTA_ENTREGA, Tipo = TiposNotaEntrega.ENTREGA, NumeroNota = 5501, Texto = "Préstamo del secador\nDevolver el viernes" });
            notas.Filas.Add(new NotaParadaReparto { Pedido = NOTA_RECOGIDA, Tipo = TiposNotaEntrega.RECOGIDA, NumeroNota = 5502, Texto = "Recoger el sillón" });
        }

        private ResolucionCodigoNota Resolver(string codigo)
        {
            if (!CodigoNotaEntrega.TryLeer(codigo, out string prefijo, out int pedido, out int? bulto))
            {
                return null;
            }
            var r = new ResolucionCodigoNota { Pedido = pedido, Bulto = bulto ?? 1, EsRecogida = prefijo == "NR" };
            if (pedido == NOTA_ENTREGA || pedido == NOTA_RECOGIDA || pedido == 929000 || pedido == 929001)
            {
                r.EsNota = true;
                EnviosAgencia envio = repositorio.Envios.Values.FirstOrDefault(e => e.Pedido == pedido);
                if (envio != null && (envio.Estado == Constantes.Agencias.ESTADO_TRAMITADO || envio.Estado == Constantes.Agencias.ESTADO_INCIDENTADO))
                {
                    r.EnvioId = envio.Numero;
                }
                else if (envio != null && envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
                {
                    r.EnvioEntregado = envio.Numero;
                }
            }
            return r;
        }

        private static CargaBultoRepartoDTO Carga(string codigo, DateTime? fechaHora = null, bool reasignar = false)
            => new CargaBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = Guid.NewGuid(), FechaHora = fechaHora, Reasignar = reasignar };

        /// <summary>Una reposición enviada con sus etiquetas R{traspaso}-{1..total}.</summary>
        private void Reposicion(int traspaso, string destino, int total, bool recibida = false)
        {
            etiquetas.Destinos[traspaso] = destino;
            for (int b = 1; b <= total; b++)
            {
                etiquetas.Filas.Add(new FilaBultoReposicion
                {
                    Empresa = "1", Traspaso = traspaso, Bulto = (short)b, Total = (short)total, Codigo = CodigosBultoReposicion.Codigo(traspaso, b),
                    FechaLectura = recibida ? AHORA.AddDays(-1) : (DateTime?)null
                });
            }
        }

        // ================================================================ códigos NE / NR

        [TestMethod]
        public async Task Bulto_LosTresFormatosDeNota_DanElEnvioDeSuParadaConSuTipoYTexto()
        {
            ConNotas();

            var ne = await servicio.Bulto("ne929667");
            var nr = await servicio.Bulto("NR929554");
            var nr2 = await servicio.Bulto("NR929554-2");

            Assert.AreEqual(TipoResultadoReparto.Ok, ne.Tipo, ne.Mensaje);
            Assert.AreEqual(251500, ne.Contenido.EnvioId);
            Assert.AreEqual(1, ne.Contenido.Bulto);
            Assert.AreEqual(TiposParadaReparto.NOTA_ENTREGA, ne.Contenido.TipoParada);
            Assert.AreEqual("NE929667", ne.Contenido.CodigoDocumento);
            Assert.AreEqual(5501, ne.Contenido.NumeroNota);
            Assert.AreEqual("Préstamo del secador\nDevolver el viernes", ne.Contenido.TextoNota);
            Assert.AreEqual(251501, nr.Contenido.EnvioId);
            Assert.AreEqual(1, nr.Contenido.Bulto);
            Assert.AreEqual(TiposParadaReparto.NOTA_RECOGIDA, nr.Contenido.TipoParada);
            Assert.AreEqual(2, nr2.Contenido.Bulto);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Bulto("NR929554-3")).Tipo, "La recogida tiene 2 bultos");
        }

        [TestMethod]
        public async Task Bulto_NotaSinParada404_YaEntregada409_NoEsNota404()
        {
            Envio(251502, 929001, estado: Constantes.Agencias.ESTADO_ENTREGADO);

            var sinParada = await servicio.Bulto("NE929000");
            var entregada = await servicio.Bulto("NR929001");
            var noEsNota = await servicio.Bulto("NE123456");

            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, sinParada.Tipo);
            Assert.AreEqual("La nota 929000 no está en el reparto: factúrela en Facturar rutas.", sinParada.Mensaje);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, entregada.Tipo);
            Assert.AreEqual("La nota 929001 ya está recogida.", entregada.Mensaje);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, noEsNota.Tipo);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Bulto("XYZ")).Tipo);
        }

        [TestMethod]
        public async Task Cargar_ConNEyNR_AsignaLaParadaAlQueEscaneaYApuntaElBulto()
        {
            ConNotas();

            var ne = await servicio.Cargar(Carga("NE929667"), ANDRE);
            var nr2 = await servicio.Cargar(Carga("NR929554-2"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, ne.Tipo, ne.Mensaje);
            Assert.AreEqual(251500, ne.Contenido.EnvioId);
            Assert.AreEqual("Andre", ne.Contenido.Repartidor);
            Assert.AreEqual(TiposParadaReparto.NOTA_ENTREGA, ne.Contenido.TipoParada);
            Assert.AreEqual("NE929667", ne.Contenido.CodigoDocumento);
            Assert.AreEqual(TiposParadaReparto.NOTA_RECOGIDA, nr2.Contenido.TipoParada);
            Assert.AreEqual(2, nr2.Contenido.Bulto);
            Assert.AreEqual(2, nr2.Contenido.Bultos);
            CollectionAssert.AreEquivalent(new[] { 251500, 251501 }, cargas.Filas.Select(f => f.EnvioId).ToList());
            Assert.AreEqual(2, cargas.Filas.Single(f => f.EnvioId == 251501).Bulto);
        }

        // ================================================================ dueño de la parada

        [TestMethod]
        public async Task Cargar_BultoDeLaParadaDeOtro_409ClaroDeUsted_YNoSeApuntaNada()
        {
            ConNotas();
            _ = await servicio.Cargar(Carga("NR929554-1"), ANDRE);

            var resultado = await servicio.Cargar(Carga("NR929554-2"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, resultado.Tipo);
            StringAssert.StartsWith(resultado.Mensaje, "Este bulto no es suyo: es de la parada de Andre.");
            Assert.AreEqual(ServicioRepartoPropio.CODIGO_ENVIO_DE_OTRO, resultado.Codigo);
            Assert.AreEqual("Andre", resultado.Detalles.GetType().GetProperty("repartidor").GetValue(resultado.Detalles));
            Assert.AreEqual(1, cargas.Filas.Count);
            Assert.AreEqual(0, avisos.Recibidos.Count(a => a.Motivo == RepartoCambiadoDTO.MOTIVO_CARGA && a.Usuario == MIGUEL), "Un error no avisa");
        }

        [TestMethod]
        public async Task Envios_SoloMios_LasSuyasYLasSinCargar_NuncaLasDeOtro_YSinSoloMiosTodas()
        {
            Envio(251300);
            Envio(251301);
            Envio(251302);
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251301-1"), MIGUEL);

            List<EnvioRepartoDTO> deMiguel = (await servicio.Envios(null, soloMios: true, usuario: "miguel")).Contenido;
            List<EnvioRepartoDTO> todas = (await servicio.Envios(null)).Contenido;

            CollectionAssert.AreEquivalent(new[] { 251301, 251302 }, deMiguel.Select(e => e.EnvioId).ToList(), "La de Andre no la ve");
            Assert.AreEqual(3, todas.Count);
            Assert.IsTrue(todas.All(e => e.TipoParada == TiposParadaReparto.ENVIO && e.TextoNota == null && e.CodigoDocumento == null));
        }

        // ================================================================ orden

        [TestMethod]
        public async Task Envios_SalenPorOrden_NulosAlFinal_LuegoFechaYNumero()
        {
            Envio(251300, fecha: AHORA.Date);
            Envio(251301, fecha: AHORA.Date.AddDays(-1));
            Envio(251302, fecha: AHORA.Date);
            Envio(251303, fecha: AHORA.Date);
            orden.Filas[251303] = new OrdenParadaReparto { ParadaId = 251303, Orden = 1, Usuario = ANDRE, FechaHora = AHORA };
            orden.Filas[251300] = new OrdenParadaReparto { ParadaId = 251300, Orden = 2, Usuario = ANDRE, FechaHora = AHORA };

            List<EnvioRepartoDTO> lista = (await servicio.Envios(null)).Contenido;

            CollectionAssert.AreEqual(new[] { 251303, 251300, 251301, 251302 }, lista.Select(e => e.EnvioId).ToList());
            Assert.AreEqual(1, lista[0].Orden);
            Assert.IsNull(lista[2].Orden);
        }

        [TestMethod]
        public async Task Orden_GuardaLaPosicion_IgnoraRepetidasYLasQueNoEstanEnReparto_YAvisa()
        {
            Envio(251300);
            Envio(251301);
            Envio(251302, estado: Constantes.Agencias.ESTADO_ENTREGADO);
            var origen = new OrigenPeticionReparto { Conexion = "c1", Dispositivo = "PDA-1" };

            var resultado = await servicio.GuardarOrden(new OrdenRepartoDTO { FechaHora = AHORA, Envios = new List<int> { 251301, 251300, 251301, 251302, 999 } }, ANDRE, origen);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(2, resultado.Contenido.Guardados);
            Assert.AreEqual(3, resultado.Contenido.Ignorados);
            Assert.AreEqual(1, orden.Filas[251301].Orden);
            Assert.AreEqual(2, orden.Filas[251300].Orden);
            Assert.IsFalse(orden.Filas.ContainsKey(251302));
            AvisoFalso aviso = avisos.Recibidos.Single();
            Assert.AreEqual(RepartoCambiadoDTO.MOTIVO_ORDEN, aviso.Motivo);
            Assert.AreSame(origen, aviso.Origen);
        }

        [TestMethod]
        public async Task Orden_UltimoGana_UnaColaQueLlegaTardeNoPisaElOrdenMasNuevo()
        {
            Envio(251300);
            Envio(251301);
            _ = await servicio.GuardarOrden(new OrdenRepartoDTO { FechaHora = AHORA, Envios = new List<int> { 251300, 251301 } }, ANDRE);

            var tarde = await servicio.GuardarOrden(new OrdenRepartoDTO { FechaHora = AHORA.AddMinutes(-5), Envios = new List<int> { 251301, 251300 } }, ANDRE);

            Assert.AreEqual(0, tarde.Contenido.Guardados);
            Assert.AreEqual(2, tarde.Contenido.Ignorados);
            Assert.AreEqual(1, orden.Filas[251300].Orden);

            var nueva = await servicio.GuardarOrden(new OrdenRepartoDTO { FechaHora = AHORA.AddMinutes(5), Envios = new List<int> { 251301, 251300 } }, ANDRE);
            Assert.AreEqual(2, nueva.Contenido.Guardados);
            Assert.AreEqual(2, orden.Filas[251300].Orden);
        }

        [TestMethod]
        public async Task Orden_EsDeCadaRepartidor_NoSePuedenOrdenarLasParadasDeOtro()
        {
            Envio(251300);
            Envio(251301);
            Envio(251302);
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251301-1"), MIGUEL);

            var deMiguel = await servicio.GuardarOrden(new OrdenRepartoDTO { FechaHora = AHORA, Envios = new List<int> { 251300, 251301, 251302 } }, MIGUEL);

            Assert.AreEqual(2, deMiguel.Contenido.Guardados, "La suya y la de nadie");
            Assert.AreEqual(1, deMiguel.Contenido.Ignorados, "La de Andre no");
            Assert.IsFalse(orden.Filas.ContainsKey(251300));
            Assert.AreEqual(2, orden.Filas[251301].Orden);
        }

        [TestMethod]
        public async Task Orden_SinScript400_YSinLista400()
        {
            orden.Script = false;
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.GuardarOrden(new OrdenRepartoDTO { Envios = new List<int>() }, ANDRE)).Tipo);
            StringAssert.Contains((await servicio.GuardarOrden(new OrdenRepartoDTO { Envios = new List<int>() }, ANDRE)).Mensaje, "Issue759_ParadasReparto.sql");
            orden.Script = true;
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.GuardarOrden(new OrdenRepartoDTO(), ANDRE)).Tipo);
        }

        // ================================================================ recoger

        [TestMethod]
        public async Task Entregar_Recogida_GuardaLosBultosRecogidosYLaHistoriaDiceRecogido_YAvisa()
        {
            ConNotas();

            var resultado = await servicio.Entregar(251501, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), BultosRecogidos = 2, Receptor = "Ana" }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(EnvioRepartoDTO.ESTADO_ENTREGADO, resultado.Contenido.Estado);
            ServicioRepartoPropioTests.Registro registro = repositorio.Registros.Single();
            Assert.AreEqual((short)2, registro.Prueba.BultosRecogidos);
            Assert.AreEqual("Reparto propio: recogido (2 bultos)", registro.Historia[0].Observaciones);
            Assert.AreEqual(Constantes.Agencias.ESTADO_ENTREGADO, repositorio.Envios[251501].Estado);
            Assert.AreEqual(RepartoCambiadoDTO.MOTIVO_ENTREGA, avisos.Recibidos.Single().Motivo);
        }

        [TestMethod]
        public async Task Entregar_EntregaNormal_IgnoraLosBultosRecogidos_YBultosFueraDeRango400()
        {
            ConNotas();

            var normal = await servicio.Entregar(251500, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), BultosRecogidos = 3 }, ANDRE);
            var mal = await servicio.Entregar(251501, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), BultosRecogidos = -1 }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, normal.Tipo, normal.Mensaje);
            Assert.IsNull(repositorio.Registros.Single().Prueba.BultosRecogidos);
            StringAssert.StartsWith(repositorio.Registros.Single().Historia[0].Observaciones, "Reparto propio: entregado");
            Assert.AreEqual(TipoResultadoReparto.Invalido, mal.Tipo);
        }

        [TestMethod]
        public async Task Incidencia_NadaQueRecoger_SoloEnRecogidas()
        {
            ConNotas();

            var enEntrega = await servicio.Incidencia(251500, new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = "NadaQueRecoger" }, ANDRE);
            var enRecogida = await servicio.Incidencia(251501, new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = "nadaquerecoger" }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Invalido, enEntrega.Tipo);
            StringAssert.Contains(enEntrega.Mensaje, "solo vale para las notas de recogida");
            Assert.AreEqual(TipoResultadoReparto.Ok, enRecogida.Tipo, enRecogida.Mensaje);
            PruebaEntregaReparto prueba = repositorio.Registros.Single().Prueba;
            Assert.AreEqual(IncidenciaRepartoDTO.NADA_QUE_RECOGER, prueba.Motivo);
            Assert.AreEqual("Reparto propio: No había nada que recoger", repositorio.Envios[251501].DetalleEstado?.Trim());
            Assert.AreEqual(RepartoCambiadoDTO.MOTIVO_INCIDENCIA, avisos.Recibidos.Single().Motivo);
        }

        // ================================================================ reposición

        [TestMethod]
        public async Task Reposicion_UnaParadaPorTiendaYDia_AunqueSeanVariosTraspasos_YCuentaLosBultos()
        {
            Reposicion(81010, "REI", 3);
            Reposicion(81011, "REI", 4);
            Reposicion(81012, "ALC", 2);

            foreach (string codigo in new[] { "R81010-1", "R81010-2", "R81010-3", "R81011-1", "R81011-2", "R81011-3", "R81011-4" })
            {
                var r = await servicio.Cargar(Carga(codigo), ANDRE);
                Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, codigo + ": " + r.Mensaje);
            }
            var alc = await servicio.Cargar(Carga("r81012'1"), ANDRE);

            Assert.AreEqual(2, reposiciones.Paradas.Count, "Reina y Alcobendas");
            ParadaReposicion reina = reposiciones.Paradas.Single(p => p.Destino == "REI");
            Assert.AreEqual(-reina.Id, (await servicio.Cargar(Carga("R81010-1"), ANDRE)).Contenido.EnvioId, "El mismo bulto otra vez, la misma parada");
            Assert.AreEqual(7, reposiciones.Cargas.Count(c => c.ParadaId == reina.Id), "Ni se duplica el bulto repetido");
            Assert.AreEqual(TiposParadaReparto.REPOSICION, alc.Contenido.TipoParada);
            Assert.AreEqual("Reposición Alcobendas", alc.Contenido.Nombre);

            List<EnvioRepartoDTO> lista = (await servicio.Envios(null, soloMios: true, usuario: ANDRE)).Contenido;
            EnvioRepartoDTO paradaReina = lista.Single(p => p.Nombre == "Reposición Reina");
            Assert.AreEqual(TiposParadaReparto.REPOSICION, paradaReina.TipoParada);
            Assert.IsTrue(paradaReina.EnvioId < 0);
            Assert.AreEqual(7, paradaReina.Bultos);
            Assert.AreEqual(7, paradaReina.BultosCargados);
            Assert.AreEqual("Andre", paradaReina.Repartidor);
            Assert.AreEqual("Reposición 81010: 3 de 3 bultos\nReposición 81011: 4 de 4 bultos", paradaReina.TextoNota);
            Assert.AreEqual(0m, paradaReina.Reembolso);
            Assert.AreEqual(1, lista.Count(p => p.Nombre == "Reposición Alcobendas"));
            Assert.AreEqual(9, avisos.Recibidos.Count(a => a.Motivo == RepartoCambiadoDTO.MOTIVO_CARGA), "Cada carga avisa (también la repetida)");
        }

        [TestMethod]
        public async Task Reposicion_OtroDiaOtraParada_YEnElMismoDiaLaDeOtroRepartidorDa409()
        {
            Reposicion(81010, "REI", 2);
            Reposicion(81011, "REI", 1);
            _ = await servicio.Cargar(Carga("R81010-1"), ANDRE);

            var deMiguel = await servicio.Cargar(Carga("R81011-1"), MIGUEL);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, deMiguel.Tipo);
            StringAssert.StartsWith(deMiguel.Mensaje, "Este bulto no es suyo: es de la parada de Andre.");

            var manana = await servicio.Cargar(Carga("R81011-1", AHORA.AddDays(1)), MIGUEL);
            Assert.AreEqual(TipoResultadoReparto.Ok, manana.Tipo, manana.Mensaje);
            Assert.AreEqual(2, reposiciones.Paradas.Count);
            Assert.AreEqual("Miguel", manana.Contenido.Repartidor);

            List<EnvioRepartoDTO> deAndre = (await servicio.Envios(AHORA.Date.AddDays(1), soloMios: true, usuario: ANDRE)).Contenido;
            Assert.AreEqual(1, deAndre.Count, "La de Miguel no la ve");
        }

        [TestMethod]
        public async Task Reposicion_EtiquetaDesconocida404_YaRecibida409_NoVaATienda409()
        {
            Reposicion(81020, "REI", 1, recibida: true);
            Reposicion(81021, "ALG", 1);

            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Cargar(Carga("R81099-1"), ANDRE)).Tipo);
            var recibida = await servicio.Cargar(Carga("R81020-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, recibida.Tipo);
            StringAssert.Contains(recibida.Mensaje, "ya se ha recibido en la tienda");
            var algete = await servicio.Cargar(Carga("R81021-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, algete.Tipo);
            StringAssert.Contains(algete.Mensaje, "no va a una tienda (va a Algete)");
            Assert.AreEqual(0, reposiciones.Paradas.Count);
        }

        [TestMethod]
        public async Task Reposicion_SeOrdenaYSeEntrega_SinCobro_IdempotenteYYaNoSeCarga()
        {
            Envio(251300);
            Reposicion(81010, "ALC", 2);
            int envioId = (await servicio.Cargar(Carga("R81010-1"), ANDRE)).Contenido.EnvioId;

            var ordenado = await servicio.GuardarOrden(new OrdenRepartoDTO { FechaHora = AHORA, Envios = new List<int> { envioId, 251300 } }, ANDRE);
            Assert.AreEqual(2, ordenado.Contenido.Guardados);
            List<EnvioRepartoDTO> lista = (await servicio.Envios(null)).Contenido;
            Assert.AreEqual(envioId, lista[0].EnvioId);
            Assert.AreEqual(1, lista[0].Orden);

            Assert.AreEqual(TipoResultadoReparto.Invalido,
                (await servicio.Entregar(envioId, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), CobroReembolso = "Efectivo" }, ANDRE)).Tipo);
            var clave = Guid.NewGuid();
            var entregada = await servicio.Entregar(envioId, new EntregaRepartoDTO { ClaveIdempotencia = clave }, ANDRE);
            var otraVez = await servicio.Entregar(envioId, new EntregaRepartoDTO { ClaveIdempotencia = clave }, ANDRE);
            var otraClave = await servicio.Entregar(envioId, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, entregada.Tipo, entregada.Mensaje);
            Assert.AreEqual(EnvioRepartoDTO.ESTADO_ENTREGADO, entregada.Contenido.Estado);
            Assert.AreEqual(envioId, entregada.Contenido.EnvioId);
            Assert.AreEqual(TipoResultadoReparto.Ok, otraVez.Tipo);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, otraClave.Tipo);
            Assert.AreEqual("La parada «Reposición Alcobendas» ya está entregada.", otraClave.Mensaje);
            Assert.IsFalse((await servicio.Envios(null)).Contenido.Any(p => p.EnvioId == envioId), "Entregada, ya no sale");
            Assert.AreEqual(0, repositorio.Registros.Count, "No va a EnviosAgenciaPruebasEntrega");

            var cargarOtraVez = await servicio.Cargar(Carga("R81010-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, cargarOtraVez.Tipo);
            StringAssert.Contains(cargarOtraVez.Mensaje, "ya se entregó en Alcobendas");
            Assert.AreEqual(TipoResultadoReparto.Invalido,
                (await servicio.Incidencia(envioId, new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = "Ausente" }, ANDRE)).Tipo);
        }

        [TestMethod]
        public async Task Reposicion_BultoCargado_DaSuParada_YSinCargar404()
        {
            Reposicion(81010, "REI", 2);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Bulto("R81010-1")).Tipo);
            _ = await servicio.Cargar(Carga("R81010-1"), ANDRE);

            var bulto = await servicio.Bulto("R81010-1");

            Assert.AreEqual(TipoResultadoReparto.Ok, bulto.Tipo, bulto.Mensaje);
            Assert.AreEqual(TiposParadaReparto.REPOSICION, bulto.Contenido.TipoParada);
            Assert.AreEqual("Reposición 81010: 1 de 2 bultos", bulto.Contenido.TextoNota);
        }

        // ================================================================ SignalR

        [TestMethod]
        public void AvisosSignalR_RepartoCambiado_AlGrupoRepartoSinLaPdaQueLoHaHecho()
        {
            ConexionesAlmacen.Entrar("c1", "Andre", "PDA-1", "Reparto");
            ConexionesAlmacen.Entrar("c2", "Miguel", "PDA-2", "Reparto");
            var recibidos = new List<RepartoCambiadoDTO>();
            var grupos = new List<string>();
            var excluidas = new List<string[]>();
            dynamic grupo = new ExpandoObject();
            grupo.repartoCambiado = new Action<RepartoCambiadoDTO>(r => recibidos.Add(r));
            IHubContext contexto = A.Fake<IHubContext>();
            IHubConnectionContext<dynamic> clientes = A.Fake<IHubConnectionContext<dynamic>>();
            A.CallTo(() => contexto.Clients).Returns(clientes);
            A.CallTo(() => clientes.Group(A<string>._, A<string[]>._))
                .ReturnsLazily((string nombre, string[] fuera) => { grupos.Add(nombre); excluidas.Add(fuera); return (object)grupo; });

            new AvisosAlmacenSignalR(() => contexto).RepartoCambiado(@"NUEVAVISION\Andre", RepartoCambiadoDTO.MOTIVO_ORDEN, AHORA,
                new OrigenPeticionReparto { Conexion = "c1", Dispositivo = "PDA-1" });

            Assert.AreEqual("Reparto", grupos.Single());
            CollectionAssert.AreEqual(new[] { "c1" }, excluidas.Single());
            Assert.AreEqual("Andre", recibidos.Single().Usuario);
            Assert.AreEqual("Orden", recibidos.Single().Motivo);
            Assert.AreEqual(AHORA, recibidos.Single().FechaHora);
            Assert.AreEqual("PDA-1", recibidos.Single().Dispositivo);
            Assert.AreEqual("Reparto", AvisosAlmacen.Normalizar(" reparto "));
        }

        [TestMethod]
        public void AvisosSignalR_SolaEnElGrupo_NoSeMandaNada()
        {
            ConexionesAlmacen.Entrar("c1", "Andre", "PDA-1", "Reparto");
            IHubContext contexto = A.Fake<IHubContext>();

            new AvisosAlmacenSignalR(() => contexto).RepartoCambiado("Andre", RepartoCambiadoDTO.MOTIVO_CARGA, AHORA, new OrigenPeticionReparto { Conexion = "c1" });

            A.CallTo(() => contexto.Clients).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cargar_AvisaConSuOrigen_YUnFalloDelAvisoNoRompeLaCarga()
        {
            Envio(251300);
            var origen = new OrigenPeticionReparto { Conexion = "c9", Dispositivo = "PDA-9" };
            avisos.Fallar = true;

            var resultado = await servicio.Cargar(Carga("RP251300-1"), ANDRE, origen);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(1, cargas.Filas.Count);
            Assert.AreEqual(1, avisos.Intentos);
        }

        // ================================================================ controlador

        [TestMethod]
        public async Task Controlador_PutOrden_PasaLaConexionDeLaCabecera()
        {
            Envio(251300);
            var controlador = new RepartoPropioController(null, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.Name, "Andre"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
                }, "prueba"))
            };
            controlador.Request.Headers.Add(AvisosAlmacen.CABECERA_CONEXION, "c7");

            HttpResponseMessage respuesta = await (await controlador.PutOrden(new OrdenRepartoDTO { FechaHora = AHORA, Envios = new List<int> { 251300 } }))
                .ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(), "\"Guardados\":1");
            Assert.AreEqual("c7", avisos.Recibidos.Single().Origen.Conexion);
        }

        // ================================================================ seguimiento público

        [TestMethod]
        public void Seguimiento_Recogida_RecogidaProgramadaYRecogido()
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(251501);
            VistaSeguimientoReparto programada = VistaSeguimientoReparto.Construir(envio, null, esRecogida: true);
            envio.Estado = Constantes.Agencias.ESTADO_ENTREGADO;
            VistaSeguimientoReparto recogido = VistaSeguimientoReparto.Construir(envio,
                new[] { new HitoEntregaPublico { Tipo = "Entrega", FechaHora = AHORA, Receptor = "Ana" } }, esRecogida: true);

            Assert.AreEqual("Recogida programada", programada.TituloEstado);
            Assert.AreEqual("Recogido", recogido.TituloEstado);
            StringAssert.Contains(PaginaHtmlSeguimientoReparto.Html(recogido), "Lo entregó <strong>Ana</strong>");
            Assert.AreEqual("En reparto", VistaSeguimientoReparto.Construir(ServicioRepartoPropioTests.Envio(251502), null).TituloEstado);
        }

        // ================================================================ dobles

        internal class NotasFalsas : IRepositorioNotasParadaReparto
        {
            public readonly List<NotaParadaReparto> Filas = new List<NotaParadaReparto>();

            public Task<List<NotaParadaReparto>> LeerNotas(IReadOnlyCollection<int> pedidos)
                => Task.FromResult(Filas.Where(f => pedidos.Contains(f.Pedido)).ToList());
        }

        internal class OrdenFalso : IRepositorioOrdenReparto
        {
            private readonly ServicioRepartoPropioTests.RepositorioFalso envios;
            public bool Script = true;
            public readonly Dictionary<int, OrdenParadaReparto> Filas = new Dictionary<int, OrdenParadaReparto>();

            public OrdenFalso(ServicioRepartoPropioTests.RepositorioFalso envios) => this.envios = envios;

            public bool ScriptAplicado() => Script;

            public Task<List<OrdenParadaReparto>> Leer(IReadOnlyCollection<int> paradas)
                => Task.FromResult(Filas.Values.Where(f => paradas.Contains(f.ParadaId)).ToList());

            public Task<HashSet<int>> Guardar(IReadOnlyList<OrdenParadaReparto> filas)
            {
                var hechas = new HashSet<int>();
                foreach (OrdenParadaReparto fila in filas)
                {
                    if (!Filas.TryGetValue(fila.ParadaId, out OrdenParadaReparto previa) || previa.FechaHora <= fila.FechaHora)
                    {
                        Filas[fila.ParadaId] = fila;
                        _ = hechas.Add(fila.ParadaId);
                    }
                }
                return Task.FromResult(hechas);
            }

            public Task<List<EstadoEnvioParada>> LeerEstadosEnvios(IReadOnlyCollection<int> ids)
                => Task.FromResult(envios.Envios.Values.Where(e => ids.Contains(e.Numero))
                    .Select(e => new EstadoEnvioParada { Numero = e.Numero, Agencia = e.Agencia, Estado = e.Estado }).ToList());
        }

        internal class ReposicionesFalsas : IRepositorioParadasReposicion
        {
            public readonly List<ParadaReposicion> Paradas = new List<ParadaReposicion>();
            public readonly List<CargaBultoParadaReposicion> Cargas = new List<CargaBultoParadaReposicion>();

            public bool ScriptAplicado() => true;

            public Task<List<ParadaReposicion>> LeerEnReparto(DateTime desde, DateTime hasta)
                => Task.FromResult(Paradas.Where(p => !p.Entregada && p.Fecha >= desde && p.Fecha < hasta).ToList());

            public Task<List<ParadaReposicion>> LeerParadas(IReadOnlyCollection<int> ids) => Task.FromResult(Paradas.Where(p => ids.Contains(p.Id)).ToList());

            public Task<List<CargaBultoParadaReposicion>> LeerCargas(IReadOnlyCollection<int> paradas)
                => Task.FromResult(Cargas.Where(c => paradas.Contains(c.ParadaId)).OrderBy(c => c.Id).ToList());

            public Task<CargaBultoParadaReposicion> LeerCargaDelBulto(string empresa, int traspaso, int bulto)
                => Task.FromResult(Cargas.Where(c => c.Traspaso == traspaso && c.Bulto == bulto).OrderBy(c => c.Id).LastOrDefault());

            public Task<CargaBultoParadaReposicion> LeerCargaPorClave(Guid clave) => Task.FromResult(Cargas.FirstOrDefault(c => c.ClaveIdempotencia == clave));

            public Task<ParadaReposicion> LeerAbierta(DateTime fecha, string destino)
                => Task.FromResult(Paradas.FirstOrDefault(p => !p.Entregada && p.Fecha == fecha.Date && p.Destino == destino));

            public Task<int> Cargar(CargaBultoParadaReposicion carga, DateTime fecha, string destino)
            {
                int parada = carga.ParadaId;
                if (parada <= 0)
                {
                    ParadaReposicion abierta = Paradas.FirstOrDefault(p => !p.Entregada && p.Fecha == fecha.Date && p.Destino == destino);
                    if (abierta == null)
                    {
                        abierta = new ParadaReposicion { Id = Paradas.Count + 1, Fecha = fecha.Date, Destino = destino, Estado = ParadaReposicion.ESTADO_EN_REPARTO, Usuario = carga.Usuario };
                        Paradas.Add(abierta);
                    }
                    parada = abierta.Id;
                }
                carga.ParadaId = parada;
                carga.Id = Cargas.Count + 1;
                Cargas.Add(carga);
                return Task.FromResult(parada);
            }

            public Task<bool> Entregar(int parada, Guid clave, DateTime fechaHora, string usuario, string receptor, string observaciones, decimal? latitud, decimal? longitud)
            {
                ParadaReposicion p = Paradas.FirstOrDefault(x => x.Id == parada && !x.Entregada);
                if (p == null)
                {
                    return Task.FromResult(false);
                }
                p.Estado = ParadaReposicion.ESTADO_ENTREGADA;
                p.ClaveEntrega = clave;
                p.FechaEntrega = fechaHora;
                p.UsuarioEntrega = usuario;
                return Task.FromResult(true);
            }
        }

        internal class BultosReposicionFalsos : IRepositorioBultosReposicion
        {
            public readonly List<FilaBultoReposicion> Filas = new List<FilaBultoReposicion>();
            public readonly Dictionary<int, string> Destinos = new Dictionary<int, string>();

            public Task<bool> ExisteTabla() => Task.FromResult(true);

            public Task<List<FilaBultoReposicion>> Leer(string empresa, int traspaso) => Task.FromResult(Filas.Where(f => f.Traspaso == traspaso).ToList());

            public Task<FilaBultoReposicion> LeerPorCodigo(string empresa, string codigo)
                => Task.FromResult(Filas.FirstOrDefault(f => f.Codigo == CodigosBultoReposicion.Normalizar(codigo)));

            public Task<OrigenDestinoReposicion> LeerOrigenDestino(string empresa, int traspaso)
                => Task.FromResult(Destinos.TryGetValue(traspaso, out string d) ? new OrigenDestinoReposicion { Origen = "ALG", Destino = d } : null);

            public Task<bool> Reemplazar(string empresa, int traspaso, int total, string usuario) => throw new NotSupportedException();
        }

        internal class AvisoFalso
        {
            public string Usuario;
            public string Motivo;
            public DateTime FechaHora;
            public OrigenPeticionReparto Origen;
        }

        internal class AvisosFalsos : IAvisosReparto
        {
            public readonly List<AvisoFalso> Recibidos = new List<AvisoFalso>();
            public bool Fallar;
            public int Intentos;

            public void RepartoCambiado(string usuario, string motivo, DateTime fechaHora, OrigenPeticionReparto origen)
            {
                Intentos++;
                if (Fallar)
                {
                    throw new InvalidOperationException("SignalR caído");
                }
                Recibidos.Add(new AvisoFalso { Usuario = usuario, Motivo = motivo, FechaHora = fechaHora, Origen = origen });
            }
        }
    }
}
