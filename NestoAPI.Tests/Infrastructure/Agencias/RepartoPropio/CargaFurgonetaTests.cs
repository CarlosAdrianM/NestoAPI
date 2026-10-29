using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using System;
using System.Collections.Generic;
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
    /// Ariadna#63 / NestoAPI#739 (sugerencia 873): la carga de la furgoneta. Cada repartidor escanea los bultos que se lleva; el
    /// primero se queda el envío («En reparto (Nombre)»), si lo lleva otro 409 con details.repartidor salvo Reasignar, el mismo
    /// bulto otra vez no es error, idempotente por ClaveIdempotencia, y sin el script 400 «falta el script» (lo demás como hoy).
    /// </summary>
    [TestClass]
    public class CargaFurgonetaTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 29, 8, 0, 0);
        private const string ANDRE = "Andre";
        private const string MIGUEL = @"NUEVAVISION\Miguel";

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private CargaFalsa cargas;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            cargas = new CargaFalsa();
            servicio = Servicio(cargas);
        }

        private ServicioRepartoPropio Servicio(IRepositorioCargaFurgoneta carga)
            => new ServicioRepartoPropio(repositorio, () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), carga);

        private static EnviosAgencia Envio(int numero = 251300, short estado = Constantes.Agencias.ESTADO_TRAMITADO, short bultos = 3, DateTime? fecha = null)
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(numero, estado, bultos: bultos);
            envio.Fecha = fecha ?? AHORA.Date;
            return envio;
        }

        private static CargaBultoRepartoDTO Carga(string codigo = "RP251300-1", Guid? clave = null, bool reasignar = false)
            => new CargaBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = clave ?? Guid.NewGuid(), Reasignar = reasignar };

        // ================================================================ sin script

        [TestMethod]
        public async Task SinScriptDeLaCarga_CargarYSinCargar400_YEnviosComoHoy()
        {
            cargas.Script = false;
            repositorio.Con(Envio());

            var cargar = await servicio.Cargar(Carga(), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Invalido, cargar.Tipo);
            StringAssert.Contains(cargar.Mensaje, "Issue739_CargaFurgoneta.sql");
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.SinCargar(null)).Tipo);
            Assert.AreEqual(0, cargas.Filas.Count);

            var envios = await servicio.Envios(null, soloMios: true, usuario: ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, envios.Tipo);
            Assert.AreEqual(1, envios.Contenido.Count, "Sin la carga no hay repartidores: soloMios no filtra");
            Assert.IsNull(envios.Contenido[0].Repartidor);
            Assert.AreEqual(0, envios.Contenido[0].BultosCargados);
        }

        [TestMethod]
        public async Task SinRepositorioDeCarga_EnviosComoAntes()
        {
            repositorio.Con(Envio());

            var envios = await Servicio(null).Envios(null);

            Assert.AreEqual(1, envios.Contenido.Count);
            Assert.IsNull(envios.Contenido[0].Repartidor);
        }

        // ================================================================ cargar

        [TestMethod]
        public async Task PrimerBulto_AsignaElEnvioAlUsuarioYDejaEnReparto()
        {
            repositorio.Con(Envio());

            var resultado = await servicio.Cargar(Carga("rp251300-1 "), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            ResultadoCargaBultoDTO dto = resultado.Contenido;
            Assert.AreEqual(251300, dto.EnvioId);
            Assert.AreEqual(1, dto.Bulto);
            Assert.AreEqual(3, dto.Bultos);
            Assert.AreEqual(1, dto.BultosCargados);
            Assert.AreEqual("Andre", dto.Repartidor);
            Assert.AreEqual("PELUQUERÍA LA BONITA", dto.Nombre);
            Assert.AreEqual("ALGETE", dto.Poblacion);
            CargaBultoReparto fila = cargas.Filas.Single();
            Assert.AreEqual(ANDRE, fila.Usuario);
            Assert.AreEqual(AHORA, fila.FechaHora, "Sin FechaHora, la del servidor");
            EnvioHistoria historia = cargas.Historias.Single();
            Assert.AreEqual("Repartidor", historia.Campo);
            Assert.AreEqual(string.Empty, historia.ValorAnterior);
            Assert.AreEqual("En reparto (Andre)", historia.Observaciones);
            Assert.AreEqual(251300, historia.NumeroEnvio);
        }

        [TestMethod]
        public async Task OtroBultoDelMismoRepartidor_SinHistoriaNueva_YCuentaLosDistintos()
        {
            repositorio.Con(Envio());
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            var fecha = new DateTime(2026, 10, 29, 7, 55, 0);
            CargaBultoRepartoDTO segunda = Carga("RP251300-2");
            segunda.FechaHora = fecha;

            var resultado = await servicio.Cargar(segunda, @"NUEVAVISION\andre");

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(2, resultado.Contenido.BultosCargados);
            Assert.AreEqual(1, cargas.Historias.Count, "Sigue siendo suyo (sin dominio y sin mayúsculas)");
            Assert.AreEqual(fecha, cargas.Filas.Last().FechaHora, "La hora de la PDA");
        }

        [TestMethod]
        public async Task MismoBultoOtraVez_NoEsErrorNiSeApuntaDosVeces()
        {
            repositorio.Con(Envio());
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            var resultado = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            Assert.AreEqual(1, resultado.Contenido.BultosCargados);
            Assert.AreEqual(1, cargas.Filas.Count);
        }

        [TestMethod]
        public async Task MismaClave_MismaRespuesta_YLaClaveDeOtroBulto409()
        {
            repositorio.Con(Envio());
            Guid clave = Guid.NewGuid();
            _ = await servicio.Cargar(Carga("RP251300-1", clave), ANDRE);

            var repetida = await servicio.Cargar(Carga("RP251300-1", clave), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, repetida.Tipo);
            Assert.AreEqual("Andre", repetida.Contenido.Repartidor);
            Assert.AreEqual(1, cargas.Filas.Count);

            var otra = await servicio.Cargar(Carga("RP251300-2", clave), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, otra.Tipo);
            StringAssert.Contains(otra.Mensaje, "ClaveIdempotencia");
        }

        [TestMethod]
        public async Task MismaClave_AunqueYaEsteEntregado_MismaRespuesta()
        {
            EnviosAgencia envio = repositorio.Con(Envio());
            Guid clave = Guid.NewGuid();
            _ = await servicio.Cargar(Carga("RP251300-1", clave), ANDRE);
            envio.Estado = Constantes.Agencias.ESTADO_ENTREGADO;

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Cargar(Carga("RP251300-1", clave), ANDRE)).Tipo);
        }

        [TestMethod]
        public async Task LoLlevaOtro_409ConElRepartidor_YNoSeApuntaNada()
        {
            repositorio.Con(Envio());
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            var resultado = await servicio.Cargar(Carga("RP251300-2"), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, resultado.Tipo);
            Assert.AreEqual("Este envío lo lleva Andre. Si se lo lleva usted, confírmelo para reasignárselo.", resultado.Mensaje);
            Assert.AreEqual(ServicioRepartoPropio.CODIGO_ENVIO_DE_OTRO, resultado.Codigo);
            Assert.AreEqual("Andre", resultado.Detalles.GetType().GetProperty("repartidor").GetValue(resultado.Detalles));
            Assert.AreEqual(1, cargas.Filas.Count);
        }

        [TestMethod]
        public async Task Reasignar_PasaAlNuevoConTraza_YCuentaSoloLoSuyo()
        {
            repositorio.Con(Envio());
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251300-2"), ANDRE);

            var resultado = await servicio.Cargar(Carga("RP251300-1", reasignar: true), MIGUEL);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual("Miguel", resultado.Contenido.Repartidor);
            Assert.AreEqual(1, resultado.Contenido.BultosCargados, "Los de Andre no cuentan: ahora lo lleva Miguel");
            EnvioHistoria traza = cargas.Historias.Last();
            Assert.AreEqual("Andre", traza.ValorAnterior);
            Assert.AreEqual("En reparto (Miguel): reasignado, antes lo llevaba Andre", traza.Observaciones);
            Assert.AreEqual(3, cargas.Filas.Count, "Las de Andre se quedan (traza)");

            var andreOtraVez = await servicio.Cargar(Carga("RP251300-3"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, andreOtraVez.Tipo);
            Assert.AreEqual("Miguel", andreOtraVez.Detalles.GetType().GetProperty("repartidor").GetValue(andreOtraVez.Detalles));
        }

        [TestMethod]
        public async Task EnvioNoEnReparto_409ConElMotivo_CodigoMalOBultoQueNoExiste404_SinClave400()
        {
            repositorio.Con(Envio(251300, Constantes.Agencias.ESTADO_ENTREGADO));
            repositorio.Con(Envio(251301, Constantes.Agencias.ESTADO_EN_CURSO));
            repositorio.Con(Envio(251302, Constantes.Agencias.ESTADO_DEVUELTO));
            repositorio.Con(Envio(251303));

            var entregado = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, entregado.Tipo);
            StringAssert.Contains(entregado.Mensaje, "ya está entregado");
            var sinTramitar = await servicio.Cargar(Carga("RP251301-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, sinTramitar.Tipo);
            StringAssert.Contains(sinTramitar.Mensaje, "todavía no está tramitado");
            var devuelto = await servicio.Cargar(Carga("RP251302-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, devuelto.Tipo);
            StringAssert.Contains(devuelto.Mensaje, "ya no está en reparto");

            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Cargar(Carga("123456"), ANDRE)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Cargar(Carga("RP999999-1"), ANDRE)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, (await servicio.Cargar(Carga("RP251303-4"), ANDRE)).Tipo, "Tiene 3 bultos");
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cargar(new CargaBultoRepartoDTO { Codigo = "RP251303-1" }, ANDRE)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Cargar(null, ANDRE)).Tipo);
            Assert.AreEqual(0, cargas.Filas.Count);
        }

        [TestMethod]
        public async Task Incidentado_SePuedeCargarParaReintentarlo()
        {
            repositorio.Con(Envio(estado: Constantes.Agencias.ESTADO_INCIDENTADO));

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Cargar(Carga(), ANDRE)).Tipo);
        }

        [TestMethod]
        public async Task CarreraConLaMismaClave_GanaLaOtraYSeDevuelveSuRespuesta()
        {
            repositorio.Con(Envio());
            Guid clave = Guid.NewGuid();
            cargas.SimularCarrera = new CargaBultoReparto { Id = 99, EnvioId = 251300, Bulto = 1, Usuario = ANDRE, ClaveIdempotencia = clave };

            var resultado = await servicio.Cargar(Carga("RP251300-1", clave), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            Assert.AreEqual(1, resultado.Contenido.BultosCargados);
        }

        // ================================================================ envíos y sin cargar

        [TestMethod]
        public async Task Envios_ConRepartidorYBultosCargados_YSoloMios()
        {
            repositorio.Con(Envio(251300));
            repositorio.Con(Envio(251301));
            repositorio.Con(Envio(251302));
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251300-2"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251301-1"), MIGUEL);

            List<EnvioRepartoDTO> todos = (await servicio.Envios(null)).Contenido;
            Assert.AreEqual(3, todos.Count);
            Assert.AreEqual("Andre", todos.Single(e => e.EnvioId == 251300).Repartidor);
            Assert.AreEqual(2, todos.Single(e => e.EnvioId == 251300).BultosCargados);
            Assert.AreEqual("Miguel", todos.Single(e => e.EnvioId == 251301).Repartidor);
            Assert.IsNull(todos.Single(e => e.EnvioId == 251302).Repartidor);
            Assert.AreEqual(0, todos.Single(e => e.EnvioId == 251302).BultosCargados);

            List<EnvioRepartoDTO> deMiguel = (await servicio.Envios(null, soloMios: true, usuario: "miguel")).Contenido;
            Assert.AreEqual(251301, deMiguel.Single().EnvioId);
        }

        [TestMethod]
        public async Task SinCargar_TramitadosDelDiaConAlgunBultoSinCargar()
        {
            repositorio.Con(Envio(251300, bultos: 2));
            repositorio.Con(Envio(251301, bultos: 1));
            repositorio.Con(Envio(251302, bultos: 2));
            repositorio.Con(Envio(251303, Constantes.Agencias.ESTADO_INCIDENTADO));
            repositorio.Con(Envio(251304, fecha: AHORA.Date.AddDays(-1)));
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251301-1"), MIGUEL);

            var resultado = await servicio.SinCargar(AHORA.Date);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            CollectionAssert.AreEquivalent(new[] { 251300, 251302 }, resultado.Contenido.Select(e => e.EnvioId).ToList());
            EnvioSinCargarDTO aMedias = resultado.Contenido.Single(e => e.EnvioId == 251300);
            Assert.AreEqual(2, aMedias.Bultos);
            Assert.AreEqual(1, aMedias.BultosCargados);
            Assert.AreEqual("Andre", aMedias.Repartidor);
            Assert.AreEqual(928000, aMedias.Pedido);
            Assert.AreEqual("PELUQUERÍA LA BONITA", aMedias.Nombre);
            Assert.IsNull(resultado.Contenido.Single(e => e.EnvioId == 251302).Repartidor);
        }

        // ================================================================ controlador: el 409 con details.repartidor

        [TestMethod]
        public async Task Controlador_LoLlevaOtro_409ConErrorCodeYDetailsRepartidor()
        {
            repositorio.Con(Envio());
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            var usuario = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "Miguel"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN)
            }, "prueba"));
            var controlador = new RepartoPropioController(null, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario
            };

            HttpResponseMessage respuesta = await (await controlador.Cargar(Carga("RP251300-2"))).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            string cuerpo = await respuesta.Content.ReadAsStringAsync();
            StringAssert.Contains(cuerpo, "\"code\":\"ENVIO_DE_OTRO_REPARTIDOR\"");
            StringAssert.Contains(cuerpo, "\"details\":{\"repartidor\":\"Andre\"}");
            StringAssert.Contains(cuerpo, "\"Message\":\"Este envío lo lleva Andre.");
        }

        // ================================================================ dobles

        internal class CargaFalsa : IRepositorioCargaFurgoneta
        {
            public bool Script = true;
            public readonly List<CargaBultoReparto> Filas = new List<CargaBultoReparto>();
            public readonly List<EnvioHistoria> Historias = new List<EnvioHistoria>();
            public CargaBultoReparto SimularCarrera;

            public bool ScriptAplicado() => Script;

            public Task<List<CargaBultoReparto>> LeerCargas(IReadOnlyCollection<int> envios)
                => Task.FromResult(Filas.Where(f => envios.Contains(f.EnvioId)).OrderBy(f => f.Id).ToList());

            public Task<CargaBultoReparto> LeerPorClave(Guid clave) => Task.FromResult(Filas.FirstOrDefault(f => f.ClaveIdempotencia == clave));

            public Task Registrar(CargaBultoReparto carga, IList<EnvioHistoria> historia)
            {
                if (SimularCarrera != null)
                {
                    Filas.Add(SimularCarrera);
                    SimularCarrera = null;
                    throw new ClaveIdempotenciaRepetidaException(null);
                }
                carga.Id = Filas.Count == 0 ? 1 : Filas.Max(f => f.Id) + 1;
                Filas.Add(carga);
                Historias.AddRange(historia ?? new List<EnvioHistoria>());
                return Task.CompletedTask;
            }
        }
    }
}
