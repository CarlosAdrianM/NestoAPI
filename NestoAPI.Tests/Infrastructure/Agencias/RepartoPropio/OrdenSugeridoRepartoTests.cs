using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
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
    /// NestoAPI#767 (Ariadna#68): coordenadas de las paradas (caché de Nominatim, nunca en la petición), recorrido con OSRM y la
    /// heurística de respaldo (vecino más próximo + 2-opt, determinista) y POST OrdenSugerido: desde la posición de la PDA (o
    /// Algete), solo las pendientes del repartidor, sin coordenadas al final, guardado como PUT Orden y con aviso SignalR.
    /// Sin red: Nominatim y OSRM son dobles.
    /// </summary>
    [TestClass]
    public class OrdenSugeridoRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 30, 8, 0, 0);
        private const string ANDRE = "Andre";
        private const string MIGUEL = @"NUEVAVISION\Miguel";

        // Cerca de Algete, Alcobendas y Madrid centro: casi en línea.
        private static readonly CoordenadasGeo CERCA_ALGETE = new CoordenadasGeo(40.600m, -3.530m);
        private static readonly CoordenadasGeo EN_ALCOBENDAS = new CoordenadasGeo(40.530m, -3.650m);
        private static readonly CoordenadasGeo EN_MADRID = new CoordenadasGeo(40.420m, -3.700m);

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private CargaFurgonetaTests.CargaFalsa cargas;
        private ParadasRepartoTests.OrdenFalso orden;
        private ParadasRepartoTests.ReposicionesFalsas reposiciones;
        private ParadasRepartoTests.AvisosFalsos avisos;
        private CoordenadasFalsas coordenadas;
        private MotorFalso motor;
        private HorariosFalsos horarios;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Preparar()
        {
            ConexionesAlmacen.Vaciar();
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            cargas = new CargaFurgonetaTests.CargaFalsa();
            orden = new ParadasRepartoTests.OrdenFalso(repositorio);
            reposiciones = new ParadasRepartoTests.ReposicionesFalsas();
            avisos = new ParadasRepartoTests.AvisosFalsos();
            coordenadas = new CoordenadasFalsas();
            motor = new MotorFalso();
            horarios = new HorariosFalsos();
            servicio = new ServicioRepartoPropio(repositorio, () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), cargas, new DependenciasParadasReparto
                {
                    NotasParada = new ParadasRepartoTests.NotasFalsas(),
                    Orden = orden,
                    Reposiciones = reposiciones,
                    BultosReposicion = new ParadasRepartoTests.BultosReposicionFalsos(),
                    Avisos = avisos,
                    Coordenadas = coordenadas,
                    Recorrido = new PlanificadorRecorrido(motor),
                    Horarios = horarios
                });
        }

        [TestCleanup]
        public void Limpiar() => ConexionesAlmacen.Vaciar();

        // ---------------------------------------------------------------- datos

        /// <summary>Un envío en reparto en esa dirección, con sus coordenadas en la caché si se dan, cargado por <paramref name="repartidor"/>.</summary>
        private async Task<EnviosAgencia> Parada(int numero, string calle, CoordenadasGeo geo, string repartidor = ANDRE)
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(numero, bultos: 1);
            envio.Direccion = calle;
            _ = repositorio.Con(envio);
            if (geo != null)
            {
                coordenadas.Con(envio, geo);
            }
            if (repartidor != null)
            {
                var carga = await servicio.Cargar(new CargaBultoRepartoDTO { Codigo = $"RP{numero}-1", ClaveIdempotencia = Guid.NewGuid() }, repartidor);
                Assert.AreEqual(TipoResultadoReparto.Ok, carga.Tipo, carga.Mensaje);
            }
            return envio;
        }

        private void ReposicionDeAndre(int id, string destino)
        {
            reposiciones.Paradas.Add(new ParadaReposicion { Id = id, Fecha = AHORA.Date, Destino = destino, Estado = ParadaReposicion.ESTADO_EN_REPARTO, Usuario = ANDRE });
            reposiciones.Cargas.Add(new CargaBultoParadaReposicion
            {
                Id = reposiciones.Cargas.Count + 1, ParadaId = id, Empresa = "1", Traspaso = 81000 + id, Bulto = 1, Total = 1, Usuario = ANDRE, FechaHora = AHORA
            });
        }

        private static List<int> Ids(OrdenSugeridoRepartoDTO r) => r.Paradas.Select(p => p.EnvioId).ToList();

        // ================================================================ heurística

        [TestMethod]
        public void Heuristica_DesdeLaPosicion_HastaAlgete_RecorreEnLinea_YEsDeterminista()
        {
            // Sale de Madrid centro y vuelve a Algete: Madrid → Alcobendas → cerca de Algete.
            var paradas = new List<CoordenadasGeo> { CERCA_ALGETE, EN_MADRID, EN_ALCOBENDAS };

            RecorridoCalculado uno = HeuristicaRutas.Calcular(new CoordenadasGeo(40.410m, -3.700m), SedesReparto.ALGETE, paradas);
            RecorridoCalculado dos = HeuristicaRutas.Calcular(new CoordenadasGeo(40.410m, -3.700m), SedesReparto.ALGETE, paradas);

            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, uno.Orden);
            CollectionAssert.AreEqual(uno.Orden, dos.Orden);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, uno.Motor);
            Assert.IsNull(uno.Minutos);
            Assert.IsTrue(uno.Kilometros > 20m && uno.Kilometros < 50m, $"{uno.Kilometros} km");
        }

        [TestMethod]
        public void Heuristica_DosOpt_DeshaceElCruceQueDejaElVecinoMasProximo()
        {
            // Un rectángulo: el vecino más próximo cruza; 2-opt lo deshace. El recorrido es cerrado (origen = destino).
            var origen = new CoordenadasGeo(40.000m, -3.000m);
            var paradas = new List<CoordenadasGeo>
            {
                new CoordenadasGeo(40.000m, -2.990m), new CoordenadasGeo(40.050m, -2.990m),
                new CoordenadasGeo(40.000m, -2.900m), new CoordenadasGeo(40.050m, -2.900m), new CoordenadasGeo(40.050m, -3.000m)
            };

            RecorridoCalculado r = HeuristicaRutas.Calcular(origen, origen, paradas);

            double km = Longitud(origen, origen, paradas, r.Orden);
            foreach (List<int> permutacion in Permutaciones(Enumerable.Range(0, paradas.Count).ToList()))
            {
                Assert.IsTrue(km <= Longitud(origen, origen, paradas, permutacion) * 1.05 + 1e-9, "2-opt deja un recorrido casi óptimo");
            }
            CollectionAssert.AreEquivalent(Enumerable.Range(0, paradas.Count).ToList(), r.Orden);
        }

        [TestMethod]
        public void Heuristica_SinParadas_YConUna()
        {
            Assert.AreEqual(0, HeuristicaRutas.Calcular(SedesReparto.ALGETE, SedesReparto.ALGETE, new List<CoordenadasGeo>()).Orden.Count);
            CollectionAssert.AreEqual(new[] { 0 }, HeuristicaRutas.Calcular(SedesReparto.ALGETE, SedesReparto.ALGETE, new List<CoordenadasGeo> { EN_MADRID }).Orden);
        }

        // ================================================================ OSRM

        [TestMethod]
        public void Osrm_Url_LonLat_DesdeElOrigenHastaAlgete_SinVolverAlOrigen()
        {
            string url = MotorRutasOsrm.Url(new CoordenadasGeo(40.41m, -3.7m), SedesReparto.ALGETE, new List<CoordenadasGeo> { EN_ALCOBENDAS });

            StringAssert.StartsWith(url, "https://router.project-osrm.org/trip/v1/driving/-3.7,40.41;-3.65,40.53;-3.521404,40.598625?");
            StringAssert.Contains(url, "roundtrip=false&source=first&destination=last");
        }

        [TestMethod]
        public void Osrm_Leer_OrdenPorWaypointIndex_KilometrosYMinutos()
        {
            const string json = @"{""code"":""Ok"",""trips"":[{""distance"":34567.8,""duration"":2730.0}],
                ""waypoints"":[{""waypoint_index"":0,""trips_index"":0},{""waypoint_index"":3,""trips_index"":0},
                {""waypoint_index"":1,""trips_index"":0},{""waypoint_index"":2,""trips_index"":0},{""waypoint_index"":4,""trips_index"":0}]}";

            RecorridoCalculado r = MotorRutasOsrm.Leer(json, 3);

            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, r.Orden);
            Assert.AreEqual(34.6m, r.Kilometros);
            Assert.AreEqual(46, r.Minutos);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_OSRM, r.Motor);
            Assert.IsNull(MotorRutasOsrm.Leer(@"{""code"":""NoTrips""}", 3));
            Assert.IsNull(MotorRutasOsrm.Leer(json, 4), "No cuadra con las paradas pedidas");
        }

        [TestMethod]
        public async Task Planificador_OsrmFallaTardaOResponseMal_UsaLaHeuristica()
        {
            var paradas = new List<CoordenadasGeo> { CERCA_ALGETE, EN_MADRID, EN_ALCOBENDAS };

            motor.Responder = (o, d, p) => null; // no responde a tiempo
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, (await new PlanificadorRecorrido(motor).Calcular(SedesReparto.ALGETE, SedesReparto.ALGETE, paradas)).Motor);
            motor.Responder = (o, d, p) => throw new HttpRequestException("caído");
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, (await new PlanificadorRecorrido(motor).Calcular(SedesReparto.ALGETE, SedesReparto.ALGETE, paradas)).Motor);
            motor.Responder = (o, d, p) => new RecorridoCalculado { Orden = new List<int> { 0, 0, 1 }, Motor = OrdenSugeridoRepartoDTO.MOTOR_OSRM };
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, (await new PlanificadorRecorrido(motor).Calcular(SedesReparto.ALGETE, SedesReparto.ALGETE, paradas)).Motor,
                "Una permutación mala no vale");
            motor.Responder = (o, d, p) => new RecorridoCalculado { Orden = new List<int> { 2, 1, 0 }, Kilometros = 50m, Minutos = 60, Motor = OrdenSugeridoRepartoDTO.MOTOR_OSRM };
            RecorridoCalculado bueno = await new PlanificadorRecorrido(motor).Calcular(SedesReparto.ALGETE, SedesReparto.ALGETE, paradas);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_OSRM, bueno.Motor);
            CollectionAssert.AreEqual(new[] { 2, 1, 0 }, bueno.Orden);
        }

        // ================================================================ Nominatim y la caché

        [TestMethod]
        public void Direccion_LaClaveNoDependeDeTildesMayusculasNiSignos_YLaCalleSeLimpiaParaBuscar()
        {
            Assert.AreEqual(new DireccionParada("C/ Río Tiétar, 11", "28110", "Algete").Clave,
                new DireccionParada("  c/ RIO TIETAR 11 ", "28110", "ALGETE").Clave);
            Assert.AreNotEqual(new DireccionParada("C/ Río Tiétar, 11", "28110", "Algete").Clave,
                new DireccionParada("C/ Río Tiétar, 12", "28110", "Algete").Clave);
            Assert.AreEqual(40, new DireccionParada(null, null, null).Clave.Length);
            Assert.IsFalse(new DireccionParada("Calle Mayor 1", null, " ").Geocodificable);

            Assert.AreEqual("Calle RUBENS 12", DireccionParada.CalleParaBuscar("C/ RUBENS, 12 - 9ºD"));
            Assert.AreEqual("Calle JUAN RAMÓN JIMÉNEZ 28", DireccionParada.CalleParaBuscar("C/ JUAN RAMÓN JIMÉNEZ, 28 1º IZQ."));
            Assert.AreEqual("Avenida de España 5", DireccionParada.CalleParaBuscar("Avda. de España, 5, local 3"));
            Assert.IsNull(DireccionParada.CalleParaBuscar("  "));
        }

        [TestMethod]
        public void Nominatim_UrlConsultaEstructuradaEnEspana_UserAgentConContacto_YLeerElPrimero()
        {
            string url = GeocodificadorNominatim.Url("Calle RUBENS 12", "28933", "MÓSTOLES");

            StringAssert.StartsWith(url, "https://nominatim.openstreetmap.org/search?street=Calle%20RUBENS%2012&postalcode=28933&city=M%C3%93STOLES");
            StringAssert.Contains(url, "countrycodes=es&format=jsonv2&limit=1");
            Assert.IsFalse(GeocodificadorNominatim.Url(null, "28933", "MÓSTOLES").Contains("street="));
            StringAssert.Contains(GeocodificadorNominatim.USER_AGENT, "carlosadrian@nuevavision.es");
            Assert.IsTrue(GeocodificadorNominatim.ESPERA_MINIMA >= TimeSpan.FromSeconds(1), "Política de Nominatim: 1 petición/s");

            ResultadoGeocodificacion r = GeocodificadorNominatim.Leer(@"[{""lat"":""40.3227514"",""lon"":""-3.8648211"",""display_name"":""x""}]");
            Assert.AreEqual(40.322751m, r.Latitud);
            Assert.AreEqual(-3.864821m, r.Longitud);
            Assert.IsNull(GeocodificadorNominatim.Leer("[]"));
        }

        [TestMethod]
        public async Task Geocodificar_SoloLasQueFaltan_ConCalleYSiNoConCp_SiNoNoEncontrada()
        {
            var cache = new RepositorioCoordenadasFalso();
            var geo = new GeocodificadorFalso();
            var yaEsta = new DireccionParada("Calle Mayor, 1", "28110", "Algete");
            cache.Filas[yaEsta.Clave] = new CoordenadasEnCache { Clave = yaEsta.Clave, Latitud = 40.6m, Longitud = -3.5m, Precision = "Direccion", Fecha = AHORA };
            var buena = new DireccionParada("C/ Rubens, 12 - 9ºD", "28933", "Móstoles");
            var soloCp = new DireccionParada("Camino sin nombre, 99", "28100", "Alcobendas");
            var nada = new DireccionParada("Ningún sitio, 1", "99999", "Atlántida");
            geo.Respuestas[("Calle Rubens 12", "28933")] = new ResultadoGeocodificacion { Latitud = 40.32m, Longitud = -3.86m };
            geo.Respuestas[((string)null, "28100")] = new ResultadoGeocodificacion { Latitud = 40.54m, Longitud = -3.64m };
            var servicioGeo = new ServicioGeocodificacion(cache, geo, ahora: () => AHORA);

            int guardadas = await servicioGeo.Geocodificar(new[] { yaEsta, buena, soloCp, nada, buena }, 10);

            Assert.AreEqual(3, guardadas);
            Assert.IsFalse(geo.Peticiones.Any(p => p.Item2 == "28110"), "La que ya está no se pide");
            Assert.AreEqual(ResultadoGeocodificacion.PRECISION_DIRECCION, cache.Filas[buena.Clave].Precision);
            Assert.AreEqual(ResultadoGeocodificacion.PRECISION_CODIGO_POSTAL, cache.Filas[soloCp.Clave].Precision);
            Assert.AreEqual(ResultadoGeocodificacion.PRECISION_NO_ENCONTRADA, cache.Filas[nada.Clave].Precision);
            Assert.IsNull(cache.Filas[nada.Clave].Latitud);
            Assert.AreEqual(1, geo.Peticiones.Count(p => p.Item1 == "Calle Rubens 12"), "Repetida, una sola vez");

            Dictionary<string, CoordenadasGeo> leidas = await servicioGeo.Leer(new[] { yaEsta, buena, nada });
            Assert.AreEqual(2, leidas.Count, "La no encontrada no da coordenadas");
        }

        [TestMethod]
        public async Task Geocodificar_NoEncontradaSeReintentaAlMes_TopePorPasada_YSiNominatimFallaParaSinApuntar()
        {
            var cache = new RepositorioCoordenadasFalso();
            var geo = new GeocodificadorFalso();
            var vieja = new DireccionParada("Calle Vieja, 1", "28001", "Madrid");
            var reciente = new DireccionParada("Calle Reciente, 1", "28002", "Madrid");
            cache.Filas[vieja.Clave] = new CoordenadasEnCache { Clave = vieja.Clave, Precision = "NoEncontrada", Fecha = AHORA.AddDays(-31) };
            cache.Filas[reciente.Clave] = new CoordenadasEnCache { Clave = reciente.Clave, Precision = "NoEncontrada", Fecha = AHORA.AddDays(-5) };
            var servicioGeo = new ServicioGeocodificacion(cache, geo, ahora: () => AHORA);

            Assert.AreEqual(1, await servicioGeo.Geocodificar(new[] { vieja, reciente }, 10));
            Assert.AreEqual(AHORA, cache.Filas[vieja.Clave].Fecha, "La vieja se ha vuelto a buscar");
            Assert.AreEqual(AHORA.AddDays(-5), cache.Filas[reciente.Clave].Fecha);

            var muchas = Enumerable.Range(1, 5).Select(i => new DireccionParada($"Calle {i}, 1", "28003", "Madrid")).ToList();
            Assert.AreEqual(2, await servicioGeo.Geocodificar(muchas, 2));

            geo.Fallar = true;
            int antes = cache.Filas.Count;
            Assert.AreEqual(0, await servicioGeo.Geocodificar(muchas, 10));
            Assert.AreEqual(antes, cache.Filas.Count, "Un corte no se apunta como «no encontrada»");
        }

        [TestMethod]
        public async Task Geocodificar_SinScript_NoHaceNada_YNoPide()
        {
            var cache = new RepositorioCoordenadasFalso { Script = false };
            var geo = new GeocodificadorFalso();
            int pedidas = 0;
            var servicioGeo = new ServicioGeocodificacion(cache, geo, () => pedidas++);

            Assert.AreEqual(0, await servicioGeo.Geocodificar(new[] { new DireccionParada("Calle 1", "28001", "Madrid") }, 10));
            Assert.AreEqual(0, (await servicioGeo.Leer(new[] { new DireccionParada("Calle 1", "28001", "Madrid") })).Count);
            servicioGeo.PedirGeocodificacion();
            Assert.AreEqual(0, pedidas);
            Assert.AreEqual(0, geo.Peticiones.Count);
        }

        // ================================================================ GET Envios con coordenadas

        [TestMethod]
        public async Task Envios_TraenLasCoordenadasDeLaCache_LasQueFaltanPidenElJob_YLasReposicionesLasDeSuTienda()
        {
            await Parada(251300, "Calle Mayor, 1", EN_MADRID);
            await Parada(251301, "Calle Nueva, 2", null);
            ReposicionDeAndre(7, Constantes.Almacenes.ALCOBENDAS);

            List<EnvioRepartoDTO> lista = (await servicio.Envios(null)).Contenido;

            EnvioRepartoDTO conGeo = lista.Single(e => e.EnvioId == 251300);
            Assert.AreEqual(EN_MADRID.Latitud, conGeo.Latitud);
            Assert.AreEqual(EN_MADRID.Longitud, conGeo.Longitud);
            Assert.IsNull(lista.Single(e => e.EnvioId == 251301).Latitud);
            EnvioRepartoDTO tienda = lista.Single(e => e.EnvioId == -7);
            Assert.AreEqual(SedesReparto.ALCOBENDAS.Latitud, tienda.Latitud);
            Assert.AreEqual(SedesReparto.ALCOBENDAS.Longitud, tienda.Longitud);
            StringAssert.StartsWith(tienda.Direccion, "C/ La Granja, 1");
            Assert.AreEqual("28108", tienda.CodigoPostal);
            Assert.AreEqual("Alcobendas (Madrid)", tienda.Poblacion);
            Assert.AreEqual("Reposición Alcobendas", tienda.Nombre);
            Assert.IsTrue(coordenadas.Pedidas > 0, "La que falta se pide al job (sin esperar)");
        }

        [TestMethod]
        public async Task Envios_SinCoordenadasConfiguradas_ComoAntes()
        {
            servicio = new ServicioRepartoPropio(repositorio, () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), cargas, new DependenciasParadasReparto { Orden = orden });
            _ = repositorio.Con(ServicioRepartoPropioTests.Envio(251300));

            List<EnvioRepartoDTO> lista = (await servicio.Envios(null)).Contenido;

            Assert.IsNull(lista.Single().Latitud);
        }

        // ================================================================ POST OrdenSugerido

        [TestMethod]
        public async Task OrdenSugerido_DesdeLaPosicionDeLaPda_SoloLasSuyasPendientes_SinCoordenadasDetras_YGuardaYAvisa()
        {
            await Parada(251300, "Calle A, 1", CERCA_ALGETE);
            await Parada(251301, "Calle B, 1", EN_MADRID);
            await Parada(251302, "Calle C, 1", null);          // sin coordenadas: detrás
            await Parada(251303, "Calle D, 1", EN_ALCOBENDAS);
            await Parada(251304, "Calle E, 1", EN_MADRID, MIGUEL); // de otro: no
            await Parada(251305, "Calle F, 1", EN_MADRID, null);   // de nadie: no
            EnviosAgencia incidentada = await Parada(251306, "Calle G, 1", EN_MADRID);
            incidentada.Estado = Constantes.Agencias.ESTADO_INCIDENTADO; // ya visitada: al final
            motor.Responder = (o, d, p) => null; // OSRM no responde: heurística
            var origen = new OrigenPeticionReparto { Conexion = "c1", Dispositivo = "PDA-1" };

            var resultado = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA, Latitud = 40.410m, Longitud = -3.700m }, ANDRE, origen);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            OrdenSugeridoRepartoDTO r = resultado.Contenido;
            CollectionAssert.AreEqual(new[] { 251301, 251303, 251300, 251302, 251306 }, Ids(r));
            CollectionAssert.AreEqual(new int?[] { 1, 2, 3, 4, 5 }, r.Paradas.Select(p => p.Orden).ToList());
            Assert.AreEqual(OrdenSugeridoRepartoDTO.ORIGEN_POSICION, r.Origen);
            Assert.AreEqual(40.410m, r.OrigenLatitud);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_HEURISTICA, r.Motor);
            Assert.AreEqual(1, r.SinCoordenadas);
            Assert.AreEqual(5, r.Guardados);
            Assert.AreEqual(1, orden.Filas[251301].Orden);
            Assert.AreEqual(5, orden.Filas[251306].Orden);
            Assert.IsFalse(orden.Filas.ContainsKey(251304) || orden.Filas.ContainsKey(251305));
            ParadasRepartoTests.AvisoFalso aviso = avisos.Recibidos.Last();
            Assert.AreEqual(RepartoCambiadoDTO.MOTIVO_ORDEN, aviso.Motivo);
            Assert.AreSame(origen, aviso.Origen);
            Assert.IsTrue(motor.Llamadas > 0, "Primero se intenta OSRM");
        }

        [TestMethod]
        public async Task OrdenSugerido_SinPosicion_DesdeAlgete_ConOsrm_YLasReposicionesConSuTienda()
        {
            await Parada(251300, "Calle A, 1", EN_MADRID);
            ReposicionDeAndre(3, Constantes.Almacenes.ALCOBENDAS);
            motor.Responder = (o, d, p) =>
            {
                Assert.AreEqual(SedesReparto.ALGETE.Latitud, o.Latitud, "Sin posición, sale de Algete");
                Assert.AreEqual(SedesReparto.ALGETE.Latitud, d.Latitud, "Y vuelve a Algete");
                int alcobendas = p.ToList().FindIndex(x => x.Latitud == SedesReparto.ALCOBENDAS.Latitud);
                return new RecorridoCalculado { Orden = new List<int> { alcobendas, 1 - alcobendas }, Kilometros = 61.2m, Minutos = 75, Motor = OrdenSugeridoRepartoDTO.MOTOR_OSRM };
            };

            var resultado = await servicio.OrdenSugerido(null, ANDRE);

            OrdenSugeridoRepartoDTO r = resultado.Contenido;
            CollectionAssert.AreEqual(new[] { -3, 251300 }, Ids(r));
            Assert.AreEqual(OrdenSugeridoRepartoDTO.ORIGEN_ALMACEN, r.Origen);
            Assert.AreEqual(SedesReparto.ALGETE.Longitud, r.OrigenLongitud);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.DESTINO_ALMACEN, r.Destino);
            Assert.AreEqual(SedesReparto.ALGETE.Latitud, r.DestinoLatitud);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_OSRM, r.Motor);
            Assert.AreEqual(61.2m, r.Kilometros);
            Assert.AreEqual(75, r.Minutos);
            Assert.AreEqual(1, orden.Filas[-3].Orden);
        }

        [TestMethod]
        public async Task OrdenSugerido_ConDestinoPersonalizado_AcabaAhi_YLaRespuestaLoDice()
        {
            // Sale de Algete y acaba en Madrid centro (p. ej. la casa del repartidor): cerca de Algete → Alcobendas → Madrid.
            await Parada(251300, "Calle A, 1", EN_MADRID);
            await Parada(251301, "Calle B, 1", CERCA_ALGETE);
            await Parada(251302, "Calle C, 1", EN_ALCOBENDAS);
            CoordenadasGeo destinoUsado = null;
            motor.Responder = (o, d, p) => { destinoUsado = d; return null; }; // OSRM no responde: heurística con el mismo destino
            var casa = new CoordenadasGeo(40.400m, -3.705m);

            var r = (await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA, DestinoLatitud = casa.Latitud, DestinoLongitud = casa.Longitud }, ANDRE)).Contenido;

            CollectionAssert.AreEqual(new[] { 251301, 251302, 251300 }, Ids(r));
            Assert.AreEqual(OrdenSugeridoRepartoDTO.DESTINO_PERSONALIZADO, r.Destino);
            Assert.AreEqual(casa.Latitud, r.DestinoLatitud);
            Assert.AreEqual(casa.Longitud, r.DestinoLongitud);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.ORIGEN_ALMACEN, r.Origen);
            Assert.AreEqual(casa.Latitud, destinoUsado.Latitud, "OSRM recibe ese destino como último punto");

            string url = MotorRutasOsrm.Url(SedesReparto.ALGETE, casa, new List<CoordenadasGeo> { EN_ALCOBENDAS });
            StringAssert.Contains(url, ";-3.705,40.4?roundtrip=false&source=first&destination=last");
        }

        [TestMethod]
        public async Task OrdenSugerido_DestinoIncompletoOFueraDeRango400()
        {
            await Parada(251300, "Calle A, 1", EN_MADRID);

            var solo = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { DestinoLatitud = 40.4m }, ANDRE);
            var fuera = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { DestinoLatitud = 40.4m, DestinoLongitud = 200m }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Invalido, solo.Tipo);
            StringAssert.Contains(solo.Mensaje, "mande DestinoLatitud y DestinoLongitud juntas");
            Assert.AreEqual(TipoResultadoReparto.Invalido, fuera.Tipo);
            Assert.IsFalse(orden.Filas.ContainsKey(251300), "Con el destino mal no se guarda nada");
        }

        [TestMethod]
        public async Task OrdenSugerido_PosicionCeroCero_ComoSinGps_YPosicionMala400()
        {
            await Parada(251300, "Calle A, 1", EN_MADRID);

            var ceros = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { Latitud = 0m, Longitud = 0m }, ANDRE);
            var sola = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { Latitud = 40.4m }, ANDRE);
            var fuera = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { Latitud = 140m, Longitud = -3.7m }, ANDRE);

            Assert.AreEqual(OrdenSugeridoRepartoDTO.ORIGEN_ALMACEN, ceros.Contenido.Origen);
            Assert.AreEqual(TipoResultadoReparto.Invalido, sola.Tipo);
            StringAssert.Contains(sola.Mensaje, "mande Latitud y Longitud juntas");
            Assert.AreEqual(TipoResultadoReparto.Invalido, fuera.Tipo);
        }

        [TestMethod]
        public async Task OrdenSugerido_NingunaConCoordenadas_LasDejaComoEstaban_YSinParadasNoGuardaNiAvisa()
        {
            var vacio = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO(), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, vacio.Tipo);
            Assert.AreEqual(0, vacio.Contenido.Paradas.Count);
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_SIN_COORDENADAS, vacio.Contenido.Motor);
            Assert.AreEqual(0, avisos.Recibidos.Count(a => a.Motivo == RepartoCambiadoDTO.MOTIVO_ORDEN));

            await Parada(251300, "Calle A, 1", null);
            await Parada(251301, "Calle B, 1", null);
            orden.Filas[251301] = new OrdenParadaReparto { ParadaId = 251301, Orden = 1, Usuario = ANDRE, FechaHora = AHORA.AddMinutes(-10) };

            var r = (await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA }, ANDRE)).Contenido;

            CollectionAssert.AreEqual(new[] { 251301, 251300 }, Ids(r), "En su orden de antes");
            Assert.AreEqual(OrdenSugeridoRepartoDTO.MOTOR_SIN_COORDENADAS, r.Motor);
            Assert.AreEqual(2, r.SinCoordenadas);
            Assert.IsNull(r.Kilometros);
            Assert.AreEqual(0, motor.Llamadas);
        }

        [TestMethod]
        public async Task OrdenSugerido_UltimoGana_UnaFechaHoraViejaNoPisa_YSinScript400()
        {
            await Parada(251300, "Calle A, 1", EN_MADRID);
            await Parada(251301, "Calle B, 1", CERCA_ALGETE);
            orden.Filas[251300] = new OrdenParadaReparto { ParadaId = 251300, Orden = 9, Usuario = ANDRE, FechaHora = AHORA.AddMinutes(5) };
            motor.Responder = (o, d, p) => null;

            var r = (await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA }, ANDRE)).Contenido;

            Assert.AreEqual(1, r.Guardados);
            Assert.AreEqual(1, r.Ignorados);
            Assert.AreEqual(9, orden.Filas[251300].Orden);

            orden.Script = false;
            var sinScript = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO(), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Invalido, sinScript.Tipo);
            StringAssert.Contains(sinScript.Mensaje, "Issue759_ParadasReparto.sql");
        }

        [TestMethod]
        public async Task Controlador_PostOrdenSugerido_200ConLasParadas_Y403SinPermiso()
        {
            await Parada(251300, "Calle A, 1", EN_MADRID);
            motor.Responder = (o, d, p) => null;
            RepartoPropioController Controlador(string rol)
            {
                var c = new RepartoPropioController(null, servicio)
                {
                    Request = new HttpRequestMessage(),
                    Configuration = new HttpConfiguration(),
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, ANDRE), new Claim(ClaimTypes.Role, rol) }, "prueba"))
                };
                c.Request.Headers.Add(AvisosAlmacen.CABECERA_CONEXION, "c7");
                return c;
            }

            HttpResponseMessage respuesta = await (await Controlador(Constantes.GruposSeguridad.ALMACEN)
                .PostOrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA, Latitud = 40.41m, Longitud = -3.70m })).ExecuteAsync(CancellationToken.None);
            HttpResponseMessage prohibido = await (await Controlador("Otro").PostOrdenSugerido(new PeticionOrdenSugeridoDTO()))
                .ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.OK, respuesta.StatusCode);
            string json = await respuesta.Content.ReadAsStringAsync();
            StringAssert.Contains(json, "\"Motor\":\"Heuristica\"");
            StringAssert.Contains(json, "\"Origen\":\"Posicion\"");
            StringAssert.Contains(json, "\"Latitud\":40.42");
            Assert.AreEqual("c7", avisos.Recibidos.Last().Origen.Conexion);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
        }

        // ================================================================ utilidades

        private static double Longitud(CoordenadasGeo origen, CoordenadasGeo destino, List<CoordenadasGeo> paradas, IList<int> orden)
        {
            var camino = new List<CoordenadasGeo> { origen };
            camino.AddRange(orden.Select(i => paradas[i]));
            camino.Add(destino);
            double km = 0;
            for (int i = 0; i < camino.Count - 1; i++)
            {
                km += HeuristicaRutas.DistanciaKm(camino[i], camino[i + 1]);
            }
            return km;
        }

        private static IEnumerable<List<int>> Permutaciones(List<int> elementos)
        {
            if (elementos.Count <= 1)
            {
                yield return new List<int>(elementos);
                yield break;
            }
            foreach (int primero in elementos)
            {
                foreach (List<int> resto in Permutaciones(elementos.Where(e => e != primero).ToList()))
                {
                    resto.Insert(0, primero);
                    yield return resto;
                }
            }
        }

        // ================================================================ NestoAPI#763: horario preferible

        /// <summary>Una parada de otro cliente (para que tenga su propio horario).</summary>
        private async Task<EnviosAgencia> ParadaDe(int numero, string cliente, string nombre, CoordenadasGeo geo, string horario)
        {
            EnviosAgencia envio = await Parada(numero, "Calle " + numero, geo);
            envio.Cliente = cliente + "     ";
            envio.Nombre = nombre;
            if (horario != null)
            {
                horarios.Con("1", cliente, "0", horario);
            }
            return envio;
        }

        [TestMethod]
        public async Task Envios_TraenElHorarioPreferible_EnteroYElDelDia_YLasReposicionesNo()
        {
            // AHORA es viernes 30/10/26.
            await ParadaDe(251300, "1001", "ESTÉTICA UNO", EN_MADRID, "L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00");
            await ParadaDe(251301, "1002", "ESTÉTICA DOS", EN_ALCOBENDAS, "S 10:00-13:00");
            await ParadaDe(251302, "1003", "ESTÉTICA TRES", CERCA_ALGETE, null);
            ReposicionDeAndre(7, Constantes.Almacenes.ALCOBENDAS);

            List<EnvioRepartoDTO> lista = (await servicio.Envios(null)).Contenido;

            EnvioRepartoDTO uno = lista.Single(e => e.EnvioId == 251300);
            Assert.AreEqual("L-V 10:00-14:00, 17:00-20:00; S 10:00-13:00", uno.HorarioPreferible);
            Assert.AreEqual("10:00-14:00, 17:00-20:00", uno.HorarioPreferibleDia);
            EnvioRepartoDTO dos = lista.Single(e => e.EnvioId == 251301);
            Assert.AreEqual("S 10:00-13:00", dos.HorarioPreferible);
            Assert.AreEqual(string.Empty, dos.HorarioPreferibleDia, "Tiene horario, pero no los viernes");
            Assert.IsNull(lista.Single(e => e.EnvioId == 251302).HorarioPreferible);
            Assert.IsNull(lista.Single(e => e.EnvioId == -7).HorarioPreferible);
            Assert.IsNull(uno.LlegadaEstimada, "La llegada solo la calcula el orden sugerido");

            List<EnvioRepartoDTO> sabado = (await servicio.Envios(AHORA.Date.AddDays(1))).Contenido;
            Assert.AreEqual("10:00-13:00", sabado.Single(e => e.EnvioId == 251301).HorarioPreferibleDia);
        }

        [TestMethod]
        public async Task OrdenSugerido_ConLosTramosDeOsrm_EstimaLaLlegada_YAvisaDeLasQueCaenFueraDeSuHorario()
        {
            await ParadaDe(251300, "1001", "ESTÉTICA UNO", EN_ALCOBENDAS, "L-V 10:00-14:00");
            await ParadaDe(251301, "1002", "ESTÉTICA DOS", EN_MADRID, "L-V 09:00-13:00");
            await ParadaDe(251302, "1003", "ESTÉTICA TRES", CERCA_ALGETE, null);
            // Orden: Alcobendas (30 min), Madrid (60 min), cerca de Algete (20 min).
            motor.Responder = (o, d, p) => new RecorridoCalculado
            {
                Orden = new List<int> { 0, 1, 2 }, Motor = OrdenSugeridoRepartoDTO.MOTOR_OSRM, Kilometros = 80m, Minutos = 120,
                MinutosTramos = new List<double> { 30, 60, 20 }
            };

            var resultado = await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA }, ANDRE);

            OrdenSugeridoRepartoDTO r = resultado.Contenido;
            CollectionAssert.AreEqual(new[] { 251300, 251301, 251302 }, Ids(r));
            Assert.AreEqual(AHORA.AddMinutes(30), r.Paradas[0].LlegadaEstimada, "08:30");
            Assert.AreEqual(AHORA.AddMinutes(30 + 5 + 60), r.Paradas[1].LlegadaEstimada, "09:35");
            Assert.AreEqual(AHORA.AddMinutes(30 + 5 + 60 + 5 + 20), r.Paradas[2].LlegadaEstimada, "10:00");
            Assert.IsTrue(r.Paradas[0].FueraDeHorario, "A las 08:30 aún no abre (10:00-14:00)");
            Assert.IsFalse(r.Paradas[1].FueraDeHorario, "A las 09:35 está abierto (09:00-13:00)");
            Assert.IsFalse(r.Paradas[2].FueraDeHorario, "Sin horario no se avisa");
            Assert.AreEqual(1, r.FueraDeHorario);
            StringAssert.Contains(r.AvisoHorario, "Una parada llegaría fuera de su horario preferible (ESTÉTICA UNO)");
            CollectionAssert.AreEqual(new[] { 251300, 251301, 251302 }, orden.Filas.OrderBy(f => f.Value.Orden).Select(f => f.Key).ToList(),
                "El horario solo avisa: no cambia el orden");
        }

        [TestMethod]
        public async Task OrdenSugerido_ConLaHeuristica_EstimaConLaLineaRecta_YDiaFuturoSaleALasNueve()
        {
            await ParadaDe(251300, "1001", "ESTÉTICA UNO", EN_ALCOBENDAS, "L-V 10:00-14:00");
            motor.Responder = (o, d, p) => null;

            var hoy = (await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA }, ANDRE)).Contenido;
            DateTime llegada = hoy.Paradas[0].LlegadaEstimada.Value;
            Assert.IsTrue(llegada > AHORA && llegada < AHORA.AddHours(1), $"Algete → Alcobendas en línea recta: {llegada:HH:mm}");
            Assert.IsTrue(hoy.Paradas[0].FueraDeHorario);

            Assert.AreEqual(9, ServicioRepartoPropio.HoraSalida(AHORA.Date.AddDays(3), AHORA).Value.Hour, "Día futuro: sale a las 9");
            Assert.AreEqual(AHORA, ServicioRepartoPropio.HoraSalida(AHORA.Date, AHORA));
            Assert.IsNull(ServicioRepartoPropio.HoraSalida(AHORA.Date.AddDays(-1), AHORA), "Un día pasado no se estima");
        }

        [TestMethod]
        public async Task OrdenSugerido_SinNadieFueraDeHorario_SinAviso_YSinHorariosConfigurados_ComoAntes()
        {
            await ParadaDe(251300, "1001", "ESTÉTICA UNO", EN_ALCOBENDAS, "L-V 08:00-20:00");
            motor.Responder = (o, d, p) => null;

            OrdenSugeridoRepartoDTO r = (await servicio.OrdenSugerido(new PeticionOrdenSugeridoDTO { FechaHora = AHORA }, ANDRE)).Contenido;

            Assert.AreEqual(0, r.FueraDeHorario);
            Assert.IsNull(r.AvisoHorario);
            Assert.IsFalse(r.Paradas[0].FueraDeHorario);
        }

        [TestMethod]
        public void Osrm_Leer_LosTramosDeCadaParada_EnMinutos()
        {
            const string json = @"{""code"":""Ok"",""trips"":[{""distance"":34567.8,""duration"":2730.0,
                ""legs"":[{""duration"":600.0},{""duration"":1200.0},{""duration"":900.0},{""duration"":30.0}]}],
                ""waypoints"":[{""waypoint_index"":0,""trips_index"":0},{""waypoint_index"":3,""trips_index"":0},
                {""waypoint_index"":1,""trips_index"":0},{""waypoint_index"":2,""trips_index"":0},{""waypoint_index"":4,""trips_index"":0}]}";

            RecorridoCalculado r = MotorRutasOsrm.Leer(json, 3);

            CollectionAssert.AreEqual(new[] { 10d, 20d, 15d }, r.MinutosTramos);
            Assert.IsNull(MotorRutasOsrm.Leer(json.Replace(@"""legs""", @"""otros"""), 3).MinutosTramos, "Sin legs, se estima con la línea recta");
        }

        [TestMethod]
        public void AvisoHorario_MasDeTres_ResumeLosDemas()
        {
            var fuera = Enumerable.Range(1, 5).Select(i => new EnvioRepartoDTO { Nombre = "C" + i }).ToList();

            Assert.AreEqual("5 paradas llegarían fuera de su horario preferible (C1, C2, C3 y 2 más). Revise el orden si puede; el horario solo es una preferencia.",
                ServicioRepartoPropio.AvisoHorario(fuera));
            Assert.IsNull(ServicioRepartoPropio.AvisoHorario(new List<EnvioRepartoDTO>()));
        }

        private class HorariosFalsos : IHorariosParadas
        {
            private readonly Dictionary<string, List<FranjaHorario>> filas = new Dictionary<string, List<FranjaHorario>>();

            public void Con(string empresa, string cliente, string contacto, string texto)
                => filas[RepositorioHorarioPreferible.Clave(empresa, cliente, contacto)] = FormatoHorarioPreferible.Leer(texto, out _);

            public Task<Dictionary<string, List<FranjaHorario>>> HorariosDe(IReadOnlyCollection<(string Empresa, string Cliente, string Contacto)> contactos)
                => Task.FromResult(contactos.Select(c => RepositorioHorarioPreferible.Clave(c.Empresa, c.Cliente, c.Contacto)).Distinct()
                    .Where(filas.ContainsKey).ToDictionary(c => c, c => filas[c]));
        }

        // ================================================================ dobles

        private class CoordenadasFalsas : ICoordenadasParadas
        {
            private readonly Dictionary<string, CoordenadasGeo> filas = new Dictionary<string, CoordenadasGeo>();
            public int Pedidas;

            public void Con(EnviosAgencia envio, CoordenadasGeo geo)
                => filas[new DireccionParada(envio.Direccion, envio.CodPostal, envio.Poblacion).Clave] = geo;

            public Task<Dictionary<string, CoordenadasGeo>> Leer(IReadOnlyCollection<DireccionParada> direcciones)
                => Task.FromResult(direcciones.Where(d => filas.ContainsKey(d.Clave)).GroupBy(d => d.Clave).ToDictionary(g => g.Key, g => filas[g.Key]));

            public void PedirGeocodificacion() => Pedidas++;
        }

        private class MotorFalso : IMotorRutas
        {
            public Func<CoordenadasGeo, CoordenadasGeo, IReadOnlyList<CoordenadasGeo>, RecorridoCalculado> Responder = (o, d, p) => null;
            public int Llamadas;

            public Task<RecorridoCalculado> Calcular(CoordenadasGeo origen, CoordenadasGeo destino, IReadOnlyList<CoordenadasGeo> paradas)
            {
                Llamadas++;
                return Task.FromResult(Responder(origen, destino, paradas));
            }
        }

        private class RepositorioCoordenadasFalso : IRepositorioCoordenadas
        {
            public bool Script = true;
            public readonly Dictionary<string, CoordenadasEnCache> Filas = new Dictionary<string, CoordenadasEnCache>();

            public bool ScriptAplicado() => Script;

            public Task<List<CoordenadasEnCache>> Leer(IReadOnlyCollection<string> claves)
                => Task.FromResult(Filas.Values.Where(f => claves.Contains(f.Clave)).ToList());

            public Task Guardar(CoordenadasEnCache fila)
            {
                Filas[fila.Clave] = fila;
                return Task.CompletedTask;
            }
        }

        private class GeocodificadorFalso : IGeocodificador
        {
            public readonly Dictionary<(string, string), ResultadoGeocodificacion> Respuestas = new Dictionary<(string, string), ResultadoGeocodificacion>();
            public readonly List<(string, string)> Peticiones = new List<(string, string)>();
            public bool Fallar;

            public string Fuente => "Falso";

            public Task<ResultadoGeocodificacion> Buscar(string calle, string codigoPostal, string poblacion)
            {
                if (Fallar)
                {
                    throw new HttpRequestException("Nominatim caído");
                }
                Peticiones.Add((calle, codigoPostal));
                return Task.FromResult(Respuestas.TryGetValue((calle, codigoPostal), out ResultadoGeocodificacion r) ? r : null);
            }
        }
    }
}
