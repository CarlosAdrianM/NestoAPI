using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Infraestructure.Ubicaciones;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#553 (fase 1, corte 1): crear, preparar y terminar una reposición tienda → Algete escribiendo lo mismo que
    /// Nesto viejo (traza real ALC → ALG del 06/10/26). El repositorio de memoria apunta cada escritura para comprobar el orden.
    /// </summary>
    [TestClass]
    public class ServicioPreparacionReposicionTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 6, 9, 32, 16);

        private RepositorioEnMemoria repositorio;
        private List<string> propuestasPedidas;
        private List<LineaPropuestaReposicionDTO> propuesta;
        private IUbicacionesReposicion huecos;
        private IUbicacionesReposicion huecosAlgete;
        private Dictionary<string, string> almacenesDeUsuario;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = new RepositorioEnMemoria();
            propuestasPedidas = new List<string>();
            propuesta = new List<LineaPropuestaReposicionDTO>();
            huecos = A.Fake<IUbicacionesReposicion>();
            huecosAlgete = A.Fake<IUbicacionesReposicion>();
            // En Algete: el primer producto cabe en el hueco 002/002/004; el resto, sin nada libre
            A.CallTo(() => huecosAlgete.Reservar(A<string>._, A<string>._, A<IReadOnlyList<LineaReservaReposicion>>._, A<string>._))
                .ReturnsLazily((string e, string o, IReadOnlyList<LineaReservaReposicion> lineas, string u) =>
                {
                    repositorio.Llamadas.Add("HUECOS reservar " + string.Join(", ", lineas.Select(l => $"{l.Producto}#{l.NumeroOrdenEntrada} x{l.Cantidad}")));
                    var resumen = new ResumenUbicaciones();
                    foreach (LineaReservaReposicion linea in lineas)
                    {
                        var reserva = new ReservaLineaReposicion { NumeroOrdenEntrada = linea.NumeroOrdenEntrada, Producto = linea.Producto, Pedida = linea.Cantidad };
                        if (linea == lineas.First())
                        {
                            reserva.Piezas.Add(new PiezaReservada { NumeroOrdenUbicacion = 322243201, Hueco = HuecoUbicacion.De("002", "002", "004"), Cantidad = linea.Cantidad });
                        }
                        resumen.Reservas.Add(reserva);
                    }
                    return Task.FromResult(resumen);
                });
            A.CallTo(() => huecosAlgete.DescontarAlTerminar(A<string>._, A<IReadOnlyList<int>>._, A<int>._, A<string>._))
                .ReturnsLazily((string e, IReadOnlyList<int> lineas, int traspaso, string u) =>
                {
                    repositorio.Llamadas.Add($"HUECOS descontar {traspaso} [{string.Join(",", lineas)}] {u}");
                    return Task.FromResult(new ResumenUbicaciones());
                });
            A.CallTo(() => huecosAlgete.AnularSalida(A<string>._, A<string>._, A<int>._, A<string>._))
                .ReturnsLazily((string e, string o, int traspaso, string u) =>
                {
                    repositorio.Llamadas.Add($"HUECOS anular {o} {traspaso} {u}");
                    var resumen = new ResumenUbicaciones();
                    resumen.Movimientos.Add(new MovimientoUbicacion { HuecoDestino = HuecoUbicacion.De("002", "002", "004"), Cantidad = 7 });
                    return Task.FromResult(resumen);
                });
            almacenesDeUsuario = new Dictionary<string, string> { ["Paloma"] = "ALC", ["Patricia"] = "REI" };
        }

        private ServicioPreparacionReposicion Servicio()
        {
            return new ServicioPreparacionReposicion(repositorio,
                (empresa, origen, destino, corte) =>
                {
                    propuestasPedidas.Add($"{empresa}|{origen}|{destino}" + (corte.HasValue ? $"|{corte:dd/MM/yyyy HH:mm}" : string.Empty));
                    return Task.FromResult(propuesta);
                },
                (origen, control) => control ? huecosAlgete : huecos,
                (empresa, usuario) => almacenesDeUsuario.TryGetValue(usuario, out string almacen) ? almacen : null,
                () => AHORA);
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal Paloma => Usuario("NUEVAVISION\\Paloma", "Tiendas");
        private static IPrincipal Andre => Usuario("NUEVAVISION\\Andre", "Almacén");

        private static CrearReposicionDTO Peticion(params (string producto, int cantidad)[] lineas)
        {
            return new CrearReposicionDTO
            {
                Empresa = "1", Origen = "alc", Destino = "ALG",
                Lineas = lineas.Select(l => new LineaCrearReposicionDTO { Producto = l.producto, Cantidad = l.cantidad }).ToList()
            };
        }

        // ---------------------------------------------------------------- Crear

        [TestMethod]
        public async Task Crear_ConLineas_InsertaUnaPorProductoEnElDiarioDeSalidaDelOrigenConAlmacenDestinoYEstado3()
        {
            ReposicionEnPreparacionDTO creada = await Servicio().Crear(Peticion(("41980", 5), ("36385", 1), ("41980", 2)), Paloma);

            Assert.AreEqual(0, propuestasPedidas.Count, "Con líneas no se pide la propuesta");
            CollectionAssert.AreEqual(new[]
            {
                "TRAN",
                "INSERT RepoAlcAlg 36385 x1 ALC→ALG 06/10/2026 NV NUEVAVISION\\Paloma",
                "INSERT RepoAlcAlg 41980 x7 ALC→ALG 06/10/2026 NV NUEVAVISION\\Paloma",
                "COMMIT"
            }, repositorio.Llamadas);
            A.CallTo(() => huecos.Reservar("1", "ALC", A<IReadOnlyList<LineaReservaReposicion>>.That.Matches(l => l.Count == 2), "NUEVAVISION\\Paloma"))
                .MustHaveHappenedOnceExactly();
            Assert.IsNull(creada.NumTraspaso, "Desde una tienda queda en preparación hasta Terminar");
            Assert.IsTrue(creada.Lineas.All(l => l.Hueco == null && !l.SinHueco), "Las tiendas no tienen huecos");
            Assert.AreEqual("ALC", creada.Origen);
            Assert.AreEqual("ALG", creada.Destino);
            Assert.AreEqual("RepoAlcAlg", creada.Diario);
            Assert.AreEqual(2, creada.Lineas.Count);
            Assert.AreEqual(8, creada.Unidades);
            Assert.AreEqual(ServicioPreparacionReposicion.Texto("ALC", "ALG"), repositorio.TextoInsertado);
            Assert.AreEqual("Traspaso por reposición de almacén ALC a ALG", repositorio.TextoInsertado);
            Assert.IsTrue(repositorio.TextoInsertado.Length <= 50);
        }

        [TestMethod]
        public async Task Crear_SinLineas_UsaLaPropuestaYDescartaLoQueNoHayQueReponer()
        {
            propuesta.Add(new LineaPropuestaReposicionDTO { Producto = "21116", CantidadReposicion = 1 });
            propuesta.Add(new LineaPropuestaReposicionDTO { Producto = "34469", CantidadReposicion = 0 });
            propuesta.Add(new LineaPropuestaReposicionDTO { Producto = "38744", CantidadReposicion = 2 });

            ReposicionEnPreparacionDTO creada = await Servicio().Crear(new CrearReposicionDTO { Origen = "ALC", Destino = "ALG" }, Paloma);

            CollectionAssert.AreEqual(new[] { "1|ALC|ALG" }, propuestasPedidas);
            CollectionAssert.AreEqual(new[] { "21116", "38744" }, creada.Lineas.Select(l => l.Producto).ToArray());
            Assert.AreEqual(3, creada.Unidades);
        }

        [TestMethod]
        public async Task Crear_SinLineasConHoraDeCorte_PideLaPropuestaConEseCorte()
        {
            // NestoAPI#577 (corte 3a): el job rellena con el instante del calendario; a mano (sin corte) se piden todos los pedidos
            propuesta.Add(new LineaPropuestaReposicionDTO { Producto = "21116", CantidadReposicion = 1 });
            var corte = new DateTime(2026, 10, 9, 10, 0, 0);

            _ = await Servicio().Crear(new CrearReposicionDTO { Origen = "ALC", Destino = "ALG" }, Paloma, corte);

            CollectionAssert.AreEqual(new[] { "1|ALC|ALG|09/10/2026 10:00" }, propuestasPedidas);
        }

        [TestMethod]
        public async Task Crear_ConInventarioEnCursoEnElOrigen_Es409SinEscribir()
        {
            repositorio.InventariosEnCurso.Add("ALC");

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Crear(Peticion(("41980", 5)), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Crear_ConOtraReposicionEnPreparacion_Es409SinEscribir()
        {
            repositorio.Lineas.Add(Fila(561483000, "21116", 1));

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<ReposicionYaEnPreparacionException>(() => Servicio().Crear(Peticion(("41980", 5)), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, "Ya hay una reposición en preparación de ALC a ALG");
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Crear_PropuestaVacia_EsUnaReposicionVaciaSinEscribir()
        {
            // NestoAPI#577 (corte 3b): tipo propio para que el job distinga «nada que mandar» (normal) de un error
            CrearReposicionDTO peticion = Peticion();
            peticion.Lineas = null;

            ReposicionVaciaException error = await Assert.ThrowsExceptionAsync<ReposicionVaciaException>(
                () => Servicio().Crear(peticion, Paloma, new DateTime(2026, 10, 6, 10, 0, 0)));

            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            StringAssert.Contains(error.Message, "No hay nada que reponer de ALC a ALG");
            Assert.AreEqual(0, repositorio.Llamadas.Count);
            CollectionAssert.AreEqual(new[] { "1|ALC|ALG|06/10/2026 10:00" }, propuestasPedidas, "La propuesta con el corte del job");
        }

        // ---------------------------------------------------------------- Crear desde Algete (control de ubicaciones)

        private static CrearReposicionDTO PeticionAlgete(params (string producto, int cantidad)[] lineas)
        {
            return new CrearReposicionDTO
            {
                Empresa = "1", Origen = "ALG", Destino = "REI",
                Lineas = lineas.Select(l => new LineaCrearReposicionDTO { Producto = l.producto, Cantidad = l.cantidad }).ToList()
            };
        }

        [TestMethod]
        public async Task Crear_DesdeAlgete_CreaReservaHuecosYLaCierraSinContabilizarEnUnaTransaccion()
        {
            repositorio.UltimoTraspaso = 80900;

            ReposicionEnPreparacionDTO creada = await Servicio().Crear(PeticionAlgete(("41980", 7), ("21116", 2)), Andre);

            CollectionAssert.AreEqual(new[]
            {
                "TRAN",
                "INSERT General 21116 x2 ALG→REI 06/10/2026 NV NUEVAVISION\\Andre",
                "INSERT General 41980 x7 ALG→REI 06/10/2026 NV NUEVAVISION\\Andre",
                // prdUbicarReposicion: la reserva va contra la línea de ENTRADA (su Nº Orden es el NºOrdenRepo)
                "HUECOS reservar 21116#900000000 x2, 41980#900000001 x7",
                // en «General» conviven las salidas de otros traspasos por recoger: solo cuentan las líneas sin número
                "COUNT otros General <>REI sin traspaso",
                "CONTADOR → 80901",
                "DELETE a cero General REI → 0",
                "UPDATE traspaso 80901 fecha 06/10/2026 9:32 General REI → 2",
                "UPDATE pedidosespeciales General REI",
                "HUECOS descontar 80901 [900000000,900000001] NUEVAVISION\\Andre",
                "INSERT salida General ALG -REI 06/10/2026 9:32 NUEVAVISION\\Andre → 2",
                "UPDATE entrada General REI → PendRepo → 2",
                "COMMIT"
            }, repositorio.Llamadas, "Sin prdExtrProducto: la salida la contabiliza Ariadna al terminar la recogida");
            Assert.AreEqual(80901, creada.NumTraspaso);
            Assert.AreEqual("ALG", creada.Origen);
            Assert.AreEqual("REI", creada.Destino);
            Assert.AreEqual("General", creada.Diario);
            Assert.AreEqual(9, creada.Unidades);
            LineaReposicionEnPreparacionDTO conHueco = creada.Lineas.Single(l => l.Producto == "21116");
            Assert.AreEqual("002/002/004", conHueco.Hueco);
            Assert.IsFalse(conHueco.SinHueco);
            LineaReposicionEnPreparacionDTO sinHueco = creada.Lineas.Single(l => l.Producto == "41980");
            Assert.IsNull(sinHueco.Hueco);
            Assert.IsTrue(sinHueco.SinHueco);
            Assert.AreEqual(7, sinHueco.UnidadesSinHueco);
            // Lo que queda: la entrada en el diario del destino con su traspaso (pendiente de recibir) y nada en preparación
            Assert.IsTrue(repositorio.Estado2.All(e => e.Diario == "PendRepo" && e.Traspaso == 80901));
            Assert.IsNull(await Servicio().LeerEnPreparacion("1", "ALG"));
        }

        [TestMethod]
        public async Task Crear_DesdeAlgete_LlevaLosNombresDeOrigenYDestinoYElTituloDelServidor()
        {
            ReposicionEnPreparacionDTO creada = await Servicio().Crear(new CrearReposicionDTO
            {
                Empresa = "1", Origen = "ALG", Destino = "ALC",
                Lineas = new List<LineaCrearReposicionDTO> { new LineaCrearReposicionDTO { Producto = "41980", Cantidad = 7 } }
            }, Andre);

            Assert.AreEqual("Algete", creada.NombreOrigen);
            Assert.AreEqual("Alcobendas", creada.NombreDestino);
            Assert.AreEqual("De Algete a Alcobendas", creada.Titulo, "NestoAPI#600: el título lo resuelve el servidor, no la app");
        }

        [TestMethod]
        public async Task LeerRuta_DevuelveLosNombresYElTituloUnaSolaVez()
        {
            RutaReposicionDTO ruta = await Servicio().LeerRuta("1", " alg ", "rei");

            Assert.AreEqual("ALG", ruta.Origen);
            Assert.AreEqual("REI", ruta.Destino);
            Assert.AreEqual("Algete", ruta.NombreOrigen);
            Assert.AreEqual("Reina", ruta.NombreDestino);
            Assert.AreEqual("De Algete a Reina", ruta.Titulo);
            Assert.AreEqual(0, repositorio.Llamadas.Count, "Solo lee");
        }

        [TestMethod]
        public async Task LeerRuta_SinNombreEnAlmacenes_UsaElCodigo()
        {
            repositorio.Nombres.Remove("ALC");

            RutaReposicionDTO ruta = await Servicio().LeerRuta("1", "ALG", "ALC");

            Assert.IsNull(ruta.NombreDestino);
            Assert.AreEqual("De Algete a ALC", ruta.Titulo);
        }

        [TestMethod]
        public async Task LeerRuta_AlmacenQueNoExiste_EsNull()
        {
            repositorio.QuitarAlmacen("REI");

            Assert.IsNull(await Servicio().LeerRuta("1", "ALG", "REI"));
        }

        [DataTestMethod]
        [DataRow("ALG", "ALG")]
        [DataRow("ALG", "XYZ")]
        public async Task LeerRuta_ConAlmacenesNoValidos_Es400(string origen, string destino)
        {
            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().LeerRuta("1", origen, destino));
        }

        [TestMethod]
        public async Task Controlador_Ruta_DevuelveLaRuta()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            A.CallTo(() => servicio.LeerRuta("1", "ALG", "ALC"))
                .Returns(new RutaReposicionDTO { Origen = "ALG", Destino = "ALC", NombreOrigen = "Algete", NombreDestino = "Alcobendas", Titulo = "De Algete a Alcobendas" });
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Andre };

            var ok = (OkNegotiatedContentResult<RutaReposicionDTO>)await controlador.GetRuta("ALG", "ALC");

            Assert.AreEqual("De Algete a Alcobendas", ok.Content.Titulo);
        }

        [TestMethod]
        public async Task Controlador_Ruta_AlmacenQueNoExiste_Es404()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            A.CallTo(() => servicio.LeerRuta(A<string>._, A<string>._, A<string>._)).Returns((RutaReposicionDTO)null);
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Andre };

            var resultado = (NegotiatedContentResult<string>)await controlador.GetRuta("ALG", "REI");

            Assert.AreEqual(HttpStatusCode.NotFound, resultado.StatusCode);
        }

        [TestMethod]
        public async Task LeerEnPreparacion_LlevaElTituloConLosNombres_YSinNombreUsaElCodigo()
        {
            _ = await Servicio().Crear(Peticion(("41980", 5)), Paloma);
            repositorio.Nombres.Remove("ALG");

            ReposicionEnPreparacionDTO enPreparacion = await Servicio().LeerEnPreparacion("1", "ALC");

            Assert.AreEqual("Alcobendas", enPreparacion.NombreOrigen);
            Assert.IsNull(enPreparacion.NombreDestino);
            Assert.AreEqual("De Alcobendas a ALG", enPreparacion.Titulo);
        }

        [TestMethod]
        public async Task Crear_DesdeAlgete_SiFallaLaReserva_NoQuedaNadaNiSeGastaNumero()
        {
            A.CallTo(() => huecosAlgete.Reservar(A<string>._, A<string>._, A<IReadOnlyList<LineaReservaReposicion>>._, A<string>._))
                .Throws(new NestoBusinessException("El hueco ha cambiado mientras se reservaba") { StatusCode = HttpStatusCode.Conflict });

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Crear(PeticionAlgete(("41980", 7)), Andre));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual("ROLLBACK", repositorio.Llamadas.Last());
            Assert.IsFalse(repositorio.Llamadas.Any(l => l.StartsWith("CONTADOR")));
            Assert.AreEqual(0, repositorio.Lineas.Count);
        }

        [TestMethod]
        public async Task Crear_DesdeAlgete_SoloAlmacenODireccion_AunqueTengaAlgeteComoAlmacenDePedidos()
        {
            almacenesDeUsuario["Beatriz"] = "ALG";

            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                Servicio().Crear(PeticionAlgete(("41980", 7)), Usuario("NUEVAVISION\\Beatriz", "Tiendas")));

            Assert.AreEqual(0, repositorio.Llamadas.Count);
            Assert.IsTrue(Servicio().PuedeEscribir(Usuario("NUEVAVISION\\Beatriz", "Tiendas"), "1", "ALG", controlUbicaciones: false));
            Assert.IsFalse(Servicio().PuedeEscribir(Usuario("NUEVAVISION\\Beatriz", "Tiendas"), "1", "ALG", controlUbicaciones: true));
            Assert.IsTrue(Servicio().PuedeEscribir(Usuario("NUEVAVISION\\Carlos", "Dirección"), "1", "ALG", controlUbicaciones: true));
        }

        // ---------------------------------------------------------------- Anular (DELETE)

        private void TraspasoPorSalir()
        {
            repositorio.Traspaso.Add(new FilaTraspasoReposicion { NumeroOrden = 1001, Producto = "41980", Cantidad = -7, Almacen = "ALG", Diario = "General", Estado = 1 });
            repositorio.Traspaso.Add(new FilaTraspasoReposicion { NumeroOrden = 900000001, Producto = "41980", Cantidad = 7, Almacen = "REI", Diario = "PendRepo", Estado = 3 });
        }

        [TestMethod]
        public async Task Anular_DevuelveLosHuecosYBorraSalidaYEntrada()
        {
            TraspasoPorSalir();

            ResultadoAnularReposicionDTO anulada = await Servicio().Anular("1", 80901, Andre);

            CollectionAssert.AreEqual(new[]
            {
                "TRAN",
                "HUECOS anular ALG 80901 NUEVAVISION\\Andre",
                "UPDATE pedidosespeciales quitar 80901",
                "DELETE traspaso 80901 → 2",
                "COMMIT"
            }, repositorio.Llamadas);
            Assert.AreEqual("ALG", anulada.Origen);
            Assert.AreEqual("REI", anulada.Destino);
            Assert.AreEqual(2, anulada.LineasBorradas);
            Assert.AreEqual(1, anulada.FilasUbicacionesDevueltas);
            CollectionAssert.AreEqual(new[] { "002/002/004" }, anulada.Huecos);
        }

        [TestMethod]
        public async Task Anular_ConLecturasEnAriadna_Es409SinTocarNada()
        {
            TraspasoPorSalir();
            repositorio.Lecturas = 3;

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anular("1", 80901, Andre));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, "Ariadna");
            Assert.AreEqual("ROLLBACK", repositorio.Llamadas.Last());
            Assert.IsFalse(repositorio.Llamadas.Any(l => l.StartsWith("HUECOS") || l.StartsWith("DELETE")));
            Assert.AreEqual(2, repositorio.Traspaso.Count);
        }

        [TestMethod]
        public async Task Anular_YaContabilizado_Es409()
        {
            TraspasoPorSalir();
            repositorio.Contabilizado = true;

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anular("1", 80901, Andre));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.IsFalse(repositorio.Llamadas.Any(l => l.StartsWith("HUECOS")));
        }

        [TestMethod]
        public async Task Anular_SinSalidaPendiente_Es404()
        {
            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anular("1", 80901, Andre));

            Assert.AreEqual(HttpStatusCode.NotFound, error.StatusCode);
        }

        [TestMethod]
        public async Task Anular_SinAlmacenNiDireccion_EsUnauthorizedSinTransaccion()
        {
            TraspasoPorSalir();

            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => Servicio().Anular("1", 80901, Paloma));

            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Controlador_Anular_SinPermiso_Es403()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            A.CallTo(() => servicio.Anular("1", 80901, A<IPrincipal>.Ignored)).Throws(new UnauthorizedAccessException("no"));
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Paloma };

            IHttpActionResult resultado = await controlador.DeleteAnular(80901);

            Assert.AreEqual(HttpStatusCode.Forbidden, ((ResponseMessageResult)resultado).Response.StatusCode);
        }

        [TestMethod]
        public void Sql_LoQueDejaElPostEsLoQueAriadnaListaPorSalir()
        {
            // El POST deja la salida en el diario de salida del origen, con Almacén = origen, cantidad negativa y NºTraspaso…
            string salida = RepositorioPreparacionReposicionSql.SQL_INSERTAR_SALIDA;
            StringAssert.Contains(salida, "SELECT p.Empresa, p.Diario, p.[Número], @p3, p.Texto, @p4, p.Grupo, -p.Cantidad");
            StringAssert.Contains(salida, "p.[NºTraspaso]");
            // … que es justo lo que Ariadna lista en Recoger (REPO) mientras no se contabiliza
            string porSalir = RepositorioPreparacionAlmacen.SQL_REPOSICIONES_POR_SALIR;
            StringAssert.Contains(porSalir, "s.[Almacén] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.[NºTraspaso] > 0 AND s.Cantidad < 0");
            // y en el diario compartido solo se miran las líneas sin número
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_LINEAS_DE_OTRO_ALMACEN, "(@p3 = 0 OR ISNULL([NºTraspaso], 0) = 0)");
        }

        [TestMethod]
        public void Sql_AnularBorraElTraspasoEnteroYLoQuitaDePedidosEspeciales()
        {
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_TRASPASO, "WITH (UPDLOCK, HOLDLOCK)");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_BORRAR_TRASPASO, "WHERE Empresa = @p0 AND [NºTraspaso] = @p1");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_QUITAR_TRASPASO_PEDIDOS_ESPECIALES, "SET [NºTraspaso] = NULL WHERE [NºTraspaso] = @p0");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_LECTURAS_DEL_TRASPASO, "TipoOrigen = 'REPO'");
        }

        [TestMethod]
        public async Task Crear_ProductoQueNoExiste_DeshaceTodoYEs400()
        {
            repositorio.ProductosInexistentes.Add("99999");

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Crear(Peticion(("41980", 5), ("99999", 1)), Paloma));

            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            StringAssert.Contains(error.Message, "99999");
            Assert.AreEqual("ROLLBACK", repositorio.Llamadas.Last());
            Assert.AreEqual(0, repositorio.Lineas.Count, "No queda nada a medias");
        }

        [DataTestMethod]
        [DataRow("ALC", "ALC")]
        [DataRow("ALC", "XYZ")]
        [DataRow("", "ALG")]
        public async Task Crear_ConAlmacenesNoValidos_Es400(string origen, string destino)
        {
            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Crear(new CrearReposicionDTO { Origen = origen, Destino = destino }, Usuario("NUEVAVISION\\Carlos", "Dirección")));

            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
        }

        // ---------------------------------------------------------------- Permisos

        [DataTestMethod]
        [DataRow("NUEVAVISION\\Paloma", "Tiendas", true)]      // su AlmacénPedidoVta es ALC
        [DataRow("NUEVAVISION\\Patricia", "Tiendas", false)]   // el suyo es REI
        [DataRow("NUEVAVISION\\Andre", "Almacén", true)]
        [DataRow("NUEVAVISION\\Carlos", "Dirección", true)]
        [DataRow("NUEVAVISION\\Laura", "Administración", false)]
        public void PuedeEscribir_QuienTieneElOrigenComoAlmacenDePedidosOAlmacenODireccion(string nombre, string grupo, bool esperado)
        {
            Assert.AreEqual(esperado, Servicio().PuedeEscribir(Usuario(nombre, grupo), "1", "ALC"));
        }

        // Ariadna para tiendas (09/10/26): desde el móvil la tienda entra por /oauth/token, sin dominio y sin grupos de
        // Windows (y sin el rol «Almacén», que le abriría todo api/Almacen). Prepara y termina la de SU tienda por su
        // AlmacénPedidoVta, igual que desde Nesto; la de otra tienda o la de Algete, no.
        [DataTestMethod]
        [DataRow("Paloma", "ALC", false, true)]
        [DataRow("Paloma", "REI", false, false)]
        [DataRow("Paloma", "ALG", true, false)]
        [DataRow("Patricia", "REI", false, true)]
        public void PuedeEscribir_UsuarioDeAriadnaSinDominioNiRoles_SoloSuTienda(string nombre, string origen, bool controlUbicaciones, bool esperado)
        {
            Assert.AreEqual(esperado, Servicio().PuedeEscribir(Usuario(nombre), "1", origen, controlUbicaciones));
        }

        [TestMethod]
        public async Task Crear_SinPermisoSobreElOrigen_EsUnauthorizedAccessSinEscribir()
        {
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                Servicio().Crear(Peticion(("41980", 5)), Usuario("NUEVAVISION\\Patricia", "Tiendas")));

            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        // ---------------------------------------------------------------- Cambiar cantidad

        [TestMethod]
        public async Task CambiarCantidad_ABajar_DevuelveElSobranteAlHuecoYDespuesCambiaLaLinea()
        {
            repositorio.Lineas.Add(Fila(561483500, "41980", 5));
            A.CallTo(() => huecos.DevolverSobrante("1", "ALC", 561483500, "41980", 3, "NUEVAVISION\\Paloma")).Invokes(() => repositorio.Llamadas.Add("HUECOS devolver 561483500 → 3"));

            ReposicionEnPreparacionDTO reposicion = await Servicio().CambiarCantidad("1", "ALC", 561483500, 3, Paloma);

            CollectionAssert.AreEqual(new[] { "TRAN", "HUECOS devolver 561483500 → 3", "UPDATE RepoAlcAlg 561483500 → 3", "COMMIT" }, repositorio.Llamadas);
            Assert.AreEqual(3, reposicion.Lineas.Single().Cantidad);
        }

        [TestMethod]
        public async Task CambiarCantidad_ACero_SePermite()
        {
            repositorio.Lineas.Add(Fila(561483000, "36385", 1));

            ReposicionEnPreparacionDTO reposicion = await Servicio().CambiarCantidad("1", "ALC", 561483000, 0, Paloma);

            Assert.AreEqual(0, reposicion.Lineas.Single().Cantidad);
        }

        [TestMethod]
        public async Task CambiarCantidad_ASubir_Es400ComoEnNestoViejo()
        {
            repositorio.Lineas.Add(Fila(561483500, "41980", 5));

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CambiarCantidad("1", "ALC", 561483500, 6, Paloma));

            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            StringAssert.Contains(error.Message, "mayor");
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task CambiarCantidad_LineaQueNoEsDeLaReposicion_Es404()
        {
            repositorio.Lineas.Add(Fila(561483500, "41980", 5));

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().CambiarCantidad("1", "ALC", 1, 2, Paloma));

            Assert.AreEqual(HttpStatusCode.NotFound, error.StatusCode);
        }

        // ---------------------------------------------------------------- Terminar

        [TestMethod]
        public async Task Terminar_HaceLasEscriturasDeNestoViejoEnSuOrdenYDevuelveElTraspaso()
        {
            repositorio.Lineas.Add(Fila(561483000, "36385", 0));
            repositorio.Lineas.Add(Fila(561482900, "38744", 0));
            repositorio.Lineas.Add(Fila(561483500, "41980", 3));
            repositorio.Lineas.Add(Fila(561483600, "21116", 1));
            repositorio.UltimoTraspaso = 80892;
            A.CallTo(() => huecos.DescontarAlTerminar("1", A<IReadOnlyList<int>>.That.IsSameSequenceAs(new[] { 561483500, 561483600 }), 80893, "NUEVAVISION\\Paloma"))
                .Invokes(() => repositorio.Llamadas.Add("HUECOS descontar 80893"));

            ResultadoTerminarReposicionDTO resultado = await Servicio().Terminar("1", "ALC", Paloma);

            CollectionAssert.AreEqual(new[]
            {
                "TRAN",
                "COUNT otros RepoAlcAlg <>ALG",
                "CONTADOR → 80893",
                "DELETE a cero RepoAlcAlg ALG → 2",
                "UPDATE traspaso 80893 fecha 06/10/2026 9:32 RepoAlcAlg ALG → 2",
                "UPDATE pedidosespeciales RepoAlcAlg ALG",
                "HUECOS descontar 80893",
                "INSERT salida RepoAlcAlg ALC -ALG 06/10/2026 9:32 NUEVAVISION\\Paloma → 2",
                "UPDATE entrada RepoAlcAlg ALG → PendRepo2 → 2",
                "prdExtrProducto 1 RepoAlcAlg NUEVAVISION\\Paloma",
                "UPDATE resto estado 1 RepoAlcAlg",
                "COMMIT"
            }, repositorio.Llamadas);
            Assert.AreEqual(80893, resultado.NumTraspaso);
            Assert.AreEqual("ALC", resultado.Origen);
            Assert.AreEqual("ALG", resultado.Destino);
            Assert.AreEqual("RepoAlcAlg", resultado.DiarioSalida);
            Assert.AreEqual("PendRepo2", resultado.DiarioEntrada);
            CollectionAssert.AreEqual(new[] { "41980", "21116" }, resultado.Lineas.Select(l => l.Producto).ToArray(), "Solo lo que tiene cantidad");
            Assert.AreEqual(4, resultado.Unidades);
            // El estado que queda en PreExtrProducto: la entrada en el diario del destino con su traspaso; la salida ya contabilizada
            Assert.IsTrue(repositorio.Estado2.All(e => e.Diario == "PendRepo2" && e.Traspaso == 80893 && e.Cantidad > 0));
            Assert.AreEqual(2, repositorio.Lineas.Count);
        }

        [TestMethod]
        public async Task Terminar_SinReposicionEnPreparacion_Es409()
        {
            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Terminar("1", "ALC", Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Terminar_ConLineasDeOtroAlmacenEnElDiario_Es409YDeshace()
        {
            repositorio.Lineas.Add(Fila(561483500, "41980", 3));
            repositorio.LineasDeOtroAlmacen = 1;

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Terminar("1", "ALC", Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual("ROLLBACK", repositorio.Llamadas.Last());
            Assert.IsFalse(repositorio.Llamadas.Any(l => l.StartsWith("CONTADOR")), "No se gasta número de traspaso");
        }

        [TestMethod]
        public async Task Terminar_SiProdExtrProductoFalla_SeDeshaceTodo()
        {
            repositorio.Lineas.Add(Fila(561483500, "41980", 3));
            repositorio.ErrorAlContabilizar = new NestoBusinessException("No se puede realizar la acción. Se quedaría un stock negativo.");

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Terminar("1", "ALC", Paloma));

            StringAssert.Contains(error.Message, "stock negativo");
            Assert.AreEqual("ROLLBACK", repositorio.Llamadas.Last());
            Assert.IsTrue(repositorio.Estado2.All(e => e.Diario == "RepoAlcAlg" && e.Traspaso == 0), "La reposición sigue en preparación");
        }

        // ---------------------------------------------------------------- Controlador

        [TestMethod]
        public void ElControlador_ExigeAutenticacion()
        {
            Assert.IsTrue(typeof(ReposicionesController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length > 0);
        }

        [TestMethod]
        public async Task Controlador_SinPermiso_Es403NoUnauthorized()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            A.CallTo(() => servicio.Terminar("1", "ALC", A<IPrincipal>.Ignored)).Throws(new UnauthorizedAccessException("no"));
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Paloma };

            IHttpActionResult resultado = await controlador.PostTerminar("ALC");

            Assert.AreEqual(HttpStatusCode.Forbidden, ((ResponseMessageResult)resultado).Response.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_Crear_Devuelve201ConLaReposicion()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            var peticion = new CrearReposicionDTO { Origen = "ALC", Destino = "ALG" };
            A.CallTo(() => servicio.Crear(peticion, A<IPrincipal>.Ignored)).Returns(new ReposicionEnPreparacionDTO { Origen = "ALC", Destino = "ALG" });
            var controlador = new ReposicionesController(null, servicio, permisoManual: PermisoManual(null))
            {
                Request = new HttpRequestMessage(), User = Usuario("NUEVAVISION\\Alfredo", "Almacén")
            };

            IHttpActionResult resultado = await controlador.PostCrear(peticion);

            var creado = (NegotiatedContentResult<ReposicionEnPreparacionDTO>)resultado;
            Assert.AreEqual(HttpStatusCode.Created, creado.StatusCode);
            Assert.AreEqual("ALG", creado.Content.Destino);
        }

        // ---------------------------------------------------------------- NestoAPI#577 (corte 3b): rellenar a mano, solo con permiso

        private static PermisoRellenarReposicionManual PermisoManual(string valorParametro) =>
            new PermisoRellenarReposicionManual(() => valorParametro);

        [TestMethod]
        public async Task Controlador_Crear_QuienNoEstaEnLaLista_Es403YNoSeCrea()
        {
            // Decisión de Carlos (08/10/26): solo el proceso automático rellena; a mano, solo Manuel, Alfredo y Carlos
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            var controlador = new ReposicionesController(null, servicio, permisoManual: PermisoManual(null))
            {
                Request = new HttpRequestMessage(), User = Paloma
            };

            IHttpActionResult resultado = await controlador.PostCrear(new CrearReposicionDTO { Origen = "ALC", Destino = "ALG" });

            HttpResponseMessage respuesta = ((ResponseMessageResult)resultado).Response;
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            StringAssert.Contains(await respuesta.Content.ReadAsStringAsync(),
                "Solo el proceso automático y las personas autorizadas pueden rellenar reposiciones a mano.");
            A.CallTo(() => servicio.Crear(A<CrearReposicionDTO>._, A<IPrincipal>._)).MustNotHaveHappened();
        }

        [DataTestMethod]
        [DataRow(null, "NUEVAVISION\\Alfredo", true, DisplayName = "Sin parámetro: la lista por defecto")]
        [DataRow(null, "manuel", true, DisplayName = "Ariadna (sin dominio) y sin distinguir mayúsculas")]
        [DataRow(null, "NUEVAVISION\\Carlos", true)]
        [DataRow(null, "NUEVAVISION\\Paloma", false)]
        [DataRow(null, "Andre", false)]
        [DataRow("Manuel; Alfredo ,Carlos,Andre", "Andre", true, DisplayName = "Separada por comas o punto y coma, con espacios")]
        [DataRow("Manuel, Carlos", "NUEVAVISION\\Alfredo", false)]
        [DataRow("", "NUEVAVISION\\Carlos", false, DisplayName = "Parámetro vacío: nadie")]
        [DataRow(null, "NUEVAVISION\\Carlosito", false, DisplayName = "Nombre completo, no prefijo")]
        public void PuedeRellenarManual_SegunLaLista(string valorParametro, string usuario, bool puede)
        {
            Assert.AreEqual(puede, PermisoManual(valorParametro).Puede(Usuario(usuario)));
        }

        [TestMethod]
        public void PuedeRellenarManual_SinUsuario_No()
        {
            Assert.IsFalse(PermisoManual(null).Puede(null));
            Assert.IsFalse(PermisoManual(null).Puede(new ClaimsPrincipal(new ClaimsIdentity())));
        }

        [TestMethod]
        public void Controlador_PuedeRellenarManual_DevuelveElBoolDelUsuarioActual()
        {
            var controlador = new ReposicionesController(null, A.Fake<IServicioPreparacionReposicion>(), permisoManual: PermisoManual(null))
            {
                Request = new HttpRequestMessage(), User = Usuario("Manuel")
            };

            Assert.IsTrue(((OkNegotiatedContentResult<bool>)controlador.GetPuedeRellenarManual()).Content);
            controlador.User = Paloma;
            Assert.IsFalse(((OkNegotiatedContentResult<bool>)controlador.GetPuedeRellenarManual()).Content);
        }

        [TestMethod]
        public async Task Controlador_EnPreparacion_SinNinguna_Es404()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            A.CallTo(() => servicio.LeerEnPreparacion("1", "ALC")).Returns((ReposicionEnPreparacionDTO)null);
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Paloma };

            Assert.IsInstanceOfType(await controlador.GetEnPreparacion("ALC"), typeof(NotFoundResult));
        }

        // ---------------------------------------------------------------- SQL

        [TestMethod]
        public void Sql_LasLineasEnPreparacionSonLasDelDiarioDeSalidaHaciaOtroAlmacenSinTraspaso()
        {
            string sql = RepositorioPreparacionReposicionSql.SQL_LINEAS_EN_PREPARACION;
            StringAssert.Contains(sql, "p.[Almacén] <> @p2");
            StringAssert.Contains(sql, "ISNULL(p.[NºTraspaso], 0) = 0");
            StringAssert.Contains(sql, "p.Estado >= 0");
            StringAssert.Contains(sql, "FROM ExtractoProducto e");
        }

        [TestMethod]
        public void Sql_InsertarLineaNaceEnEstado3ConAlmacenDestinoDelegacionOrigenYFormaVentaTie()
        {
            string sql = RepositorioPreparacionReposicionSql.SQL_INSERTAR_LINEA;
            StringAssert.Contains(sql, "'TIE', 1, 3, @p8");
            StringAssert.Contains(sql, "FROM Productos pr WHERE pr.Empresa = @p1 AND pr.[Número] = @p9");
        }

        [TestMethod]
        public void Sql_TerminarReproduceLasSentenciasDeNestoViejo()
        {
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_BORRAR_A_CERO, "Estado >= 0 AND Cantidad = 0");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_ASIGNAR_TRASPASO, "SET [NºTraspaso] = @p3, Fecha = @p4");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_PEDIDOS_ESPECIALES, "e.Vendedor IS NULL");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_INSERTAR_SALIDA, "-p.Cantidad");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_INSERTAR_SALIDA, "p.[NºTraspaso], p.Vendedor, 1, @p5");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_MOVER_ENTRADA, "SET Diario = @p3");
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_MARCAR_RESTO, "[Número] <> 'R'");
        }

        // ---------------------------------------------------------------- NestoAPI#577 (corte 3a): cabecera ReposicionesTraspasos

        private RegistroEnMemoria registro;

        private ServicioPreparacionReposicion ServicioConCabecera()
        {
            registro = new RegistroEnMemoria(repositorio);
            return new ServicioPreparacionReposicion(repositorio,
                (empresa, origen, destino, corte) => Task.FromResult(propuesta),
                (origen, control) => control ? huecosAlgete : huecos,
                (empresa, usuario) => almacenesDeUsuario.TryGetValue(usuario, out string almacen) ? almacen : null,
                () => AHORA,
                registro);
        }

        [TestMethod]
        public async Task Crear_DesdeUnaTienda_GuardaLaCabeceraSinNumeroDentroDeLaTransaccion_YNoGastaNumeroDelContador()
        {
            // NestoAPI#577 (08/10/26, Carlos): no se reservan números. En la tienda la reposición se queda en preparación SIN
            // número (hasta Terminar) y su cabecera también: el número lo pone Terminar.
            ReposicionEnPreparacionDTO creada = await ServicioConCabecera().Crear(Peticion(("41980", 5)), Paloma);

            CollectionAssert.AreEqual(new[]
            {
                "TRAN",
                "INSERT RepoAlcAlg 41980 x5 ALC→ALG 06/10/2026 NV NUEVAVISION\\Paloma",
                "CABECERA crear - ALC→ALG Nesto NUEVAVISION\\Paloma 06/10/2026 9:32 corte -",
                "COMMIT"
            }, repositorio.Llamadas);
            Assert.IsNull(creada.NumTraspaso);
            Assert.IsNull(registro.Cabeceras.Single().NumTraspaso);
        }

        [TestMethod]
        public async Task Crear_DesdeAlgete_GuardaLaCabeceraConElNumeroDelTraspaso_YLaHerramientaQueDiga()
        {
            repositorio.UltimoTraspaso = 80900;
            CrearReposicionDTO peticion = PeticionAlgete(("41980", 7));
            peticion.Herramienta = "ariadna";

            _ = await ServicioConCabecera().Crear(peticion, Andre);

            Assert.AreEqual("CABECERA crear 80901 ALG→REI Ariadna NUEVAVISION\\Andre 06/10/2026 9:32 corte -", repositorio.Llamadas[repositorio.Llamadas.Count - 2]);
            Assert.AreEqual("COMMIT", repositorio.Llamadas.Last());
        }

        [TestMethod]
        public async Task Crear_ElJobConHoraDeCorte_GuardaElCorteYLaHerramientaAutomatico()
        {
            repositorio.UltimoTraspaso = 80900;
            CrearReposicionDTO peticion = PeticionAlgete(("41980", 7));
            peticion.Herramienta = "Automático";

            _ = await ServicioConCabecera().Crear(peticion, Andre, new DateTime(2026, 10, 6, 9, 0, 0));

            CabeceraReposicionTraspaso cabecera = registro.Cabeceras.Single();
            Assert.AreEqual(HerramientasReposicion.AUTOMATICO, cabecera.Herramienta);
            Assert.AreEqual(new DateTime(2026, 10, 6, 9, 0, 0), cabecera.FechaCorte);
            Assert.AreEqual(80901, cabecera.NumTraspaso);
        }

        [TestMethod]
        public async Task Crear_SinHerramientaYUsuarioSinDominio_EsAriadna()
        {
            // Ariadna entra por /oauth/token con el usuario del Identity, sin dominio
            repositorio.UltimoTraspaso = 80900;

            _ = await ServicioConCabecera().Crear(PeticionAlgete(("41980", 7)), Usuario("Andre", "Almacén"));

            Assert.AreEqual(HerramientasReposicion.ARIADNA, registro.Cabeceras.Single().Herramienta);
            Assert.AreEqual("Andre", registro.Cabeceras.Single().UsuarioCreacion);
        }

        [TestMethod]
        public async Task Crear_ConUnaHerramientaQueNoExiste_Es400SinEscribirNada()
        {
            CrearReposicionDTO peticion = Peticion(("41980", 5));
            peticion.Herramienta = "Excel";

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioConCabecera().Crear(peticion, Paloma));

            StringAssert.Contains(error.Message, "Excel");
            Assert.AreEqual(0, repositorio.Llamadas.Count);
            Assert.AreEqual(0, registro.Cabeceras.Count);
        }

        [TestMethod]
        public async Task Terminar_CogeElNumeroDelContador_YSeLoPoneALaCabeceraAbiertaDelOrigenAlMarcarlaPreparada()
        {
            repositorio.Lineas.Add(Fila(561483500, "41980", 3));
            repositorio.UltimoTraspaso = 80960;

            ResultadoTerminarReposicionDTO resultado = await ServicioConCabecera().Terminar("1", "ALC", Usuario("NUEVAVISION\\Andre", "Almacén"));

            Assert.AreEqual(80961, resultado.NumTraspaso);
            CollectionAssert.Contains(repositorio.Llamadas, "CONTADOR → 80961");
            CollectionAssert.Contains(repositorio.Llamadas, "UPDATE traspaso 80961 fecha 06/10/2026 9:32 RepoAlcAlg ALG → 1");
            // La cabecera abierta (sin número ni preparada) de ALC → ALG: si no hay (la creó Nesto viejo), no hace nada
            Assert.AreEqual("CABECERA numerar ALC→ALG 80961 NUEVAVISION\\Andre", repositorio.Llamadas[repositorio.Llamadas.Count - 2]);
            Assert.AreEqual("COMMIT", repositorio.Llamadas.Last());
        }

        [TestMethod]
        public async Task Anular_BorraTambienLaCabeceraEnLaMismaTransaccion()
        {
            TraspasoPorSalir();

            _ = await ServicioConCabecera().Anular("1", 80901, Andre);

            Assert.AreEqual("CABECERA borrar 80901", repositorio.Llamadas[repositorio.Llamadas.Count - 2]);
            Assert.AreEqual("COMMIT", repositorio.Llamadas.Last());
        }

        [DataTestMethod]
        [DataRow(null, "NUEVAVISION\\Paloma", "Nesto")]
        [DataRow(null, "Andre", "Ariadna")]
        [DataRow("", "Andre", "Ariadna")]
        [DataRow("NESTO", "Andre", "Nesto")]
        [DataRow(" ariadna ", "NUEVAVISION\\Paloma", "Ariadna")]
        [DataRow("Automático", "NUEVAVISION\\Paloma", "Automatico")]
        [DataRow("automatico", "Andre", "Automatico")]
        public void Herramienta_LaQueDigaOLaDelUsuario(string pedida, string usuario, string esperada)
        {
            Assert.AreEqual(esperada, HerramientasReposicion.Resolver(pedida, Usuario(usuario)));
        }

        [TestMethod]
        public void Sql_LaCabeceraNoFallaSinLaTabla_YTerminarNumeraLaAbiertaMasReciente()
        {
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_CREAR, "IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_MARCAR_PREPARADA, "IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_MARCAR_RECIBIDA, "IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_BORRAR, "IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_MARCAR_PREPARADA, "FechaPreparada IS NULL");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_MARCAR_RECIBIDA, "FechaRecibida IS NULL");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_NUMERAR_ABIERTA, "IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NOT NULL");
            // La abierta: sin número y sin preparar, del origen y destino; si hubiera varias (no debería), la más reciente
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_NUMERAR_ABIERTA, "a.NumTraspaso IS NULL AND a.FechaPreparada IS NULL");
            StringAssert.Contains(RegistroReposicionesTraspasosSql.SQL_NUMERAR_ABIERTA, "ORDER BY a.FechaCreacion DESC, a.Id DESC");
        }

        // ---------------------------------------------------------------- Sugerencia 581: reposición puntual tienda → Algete

        private readonly List<string> bloqueos = new List<string>();

        /// <summary>Sugerencia 581: el viaje de hoy (por defecto, con viaje, relleno y sin recoger). Se crea en cada prueba.</summary>
        private ViajesEnMemoria viajes = new ViajesEnMemoria();

        /// <summary>Lo que pasa mientras la puntual espera el bloqueo (otra petición que se adelanta).</summary>
        private Action alBloquear;

        private ServicioPreparacionReposicion ServicioPuntual(IStockLibreReposicionPuntual stockLibre = null,
            CalculadoraFechaReposicion calculadora = null)
        {
            registro = new RegistroEnMemoria(repositorio);
            return new ServicioPreparacionReposicion(repositorio,
                (empresa, origen, destino, corte) =>
                {
                    propuestasPedidas.Add($"{empresa}|{origen}|{destino}");
                    return Task.FromResult(propuesta);
                },
                (origen, control) => control ? huecosAlgete : huecos,
                (empresa, usuario) => almacenesDeUsuario.TryGetValue(usuario, out string almacen) ? almacen : null,
                () => AHORA,
                registro,
                (empresa, origen, dia) =>
                {
                    repositorio.Llamadas.Add($"BLOQUEO {empresa} {origen} {dia:dd/MM/yyyy}");
                    bloqueos.Add($"{origen} {dia:dd/MM/yyyy}");
                    alBloquear?.Invoke();
                    return Task.FromResult<IDisposable>(new Soltar(() => repositorio.Llamadas.Add("SOLTAR")));
                },
                viajes,
                stockLibre,
                calculadora);
        }

        private sealed class Soltar : IDisposable
        {
            private readonly Action alSoltar;
            public Soltar(Action alSoltar) => this.alSoltar = alSoltar;
            public void Dispose() => alSoltar();
        }

        [TestMethod]
        public async Task Puntual_CreaYTerminaDeUnaVezConSoloLoPedido_ConCabeceraPuntualPreparada_YDentroDelBloqueoDelJob()
        {
            repositorio.Stocks["41980"] = 4;
            repositorio.Stocks["36385"] = 1;
            repositorio.UltimoTraspaso = 80950;

            ResultadoTerminarReposicionDTO resultado = await ServicioPuntual().CrearPuntual(Peticion(("41980", 2), ("36385", 1), ("41980", 1)), Paloma);

            Assert.AreEqual(0, propuestasPedidas.Count, "La puntual nunca pide la propuesta");
            CollectionAssert.AreEqual(new[]
            {
                "BLOQUEO 1 ALC 06/10/2026",
                "TRAN",
                "INSERT RepoAlcAlg 36385 x1 ALC→ALG 06/10/2026 NV NUEVAVISION\\Paloma",
                "INSERT RepoAlcAlg 41980 x3 ALC→ALG 06/10/2026 NV NUEVAVISION\\Paloma",
                "COUNT otros RepoAlcAlg <>ALG",
                "CONTADOR → 80951",
                "DELETE a cero RepoAlcAlg ALG → 0",
                "UPDATE traspaso 80951 fecha 06/10/2026 9:32 RepoAlcAlg ALG → 2",
                "UPDATE pedidosespeciales RepoAlcAlg ALG",
                "INSERT salida RepoAlcAlg ALC -ALG 06/10/2026 9:32 NUEVAVISION\\Paloma → 2",
                "UPDATE entrada RepoAlcAlg ALG → PendRepo2 → 2",
                "prdExtrProducto 1 RepoAlcAlg NUEVAVISION\\Paloma",
                "UPDATE resto estado 1 RepoAlcAlg",
                "CABECERA crear 80951 ALC→ALG Puntual NUEVAVISION\\Paloma 06/10/2026 9:32 corte -",
                "CABECERA preparada 80951 NUEVAVISION\\Paloma",
                "COMMIT",
                "SOLTAR"
            }, repositorio.Llamadas);
            Assert.AreEqual(80951, resultado.NumTraspaso);
            Assert.AreEqual("ALC", resultado.Origen);
            Assert.AreEqual("ALG", resultado.Destino);
            Assert.AreEqual("PendRepo2", resultado.DiarioEntrada);
            Assert.AreEqual(4, resultado.Unidades);
            // Algete la recibe como cualquier otra: entrada con número en su diario de entrada; nada queda en preparación
            Assert.IsTrue(repositorio.Estado2.All(e => e.Diario == "PendRepo2" && e.Traspaso == 80951));
            Assert.IsNull(await Servicio().LeerEnPreparacion("1", "ALC"));
            CabeceraReposicionTraspaso cabecera = registro.Cabeceras.Single();
            Assert.AreEqual(HerramientasReposicion.PUNTUAL, cabecera.Herramienta);
            Assert.IsNull(cabecera.FechaCorte, "No es de ningún viaje del calendario");
        }

        [TestMethod]
        public async Task Puntual_ConLaDelCalendarioEnPreparacion_Es409SinEscribirNada()
        {
            // La de las 9:00 rellena y sin mandar: que la mande (o meta el producto en esa caja) antes
            repositorio.Lineas.Add(Fila(561483500, "41980", 3));
            repositorio.Stocks["36385"] = 5;

            ReposicionYaEnPreparacionException error = await Assert.ThrowsExceptionAsync<ReposicionYaEnPreparacionException>(
                () => ServicioPuntual().CrearPuntual(Peticion(("36385", 1)), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, "en preparación");
            StringAssert.StartsWith(error.Message, "Primero envía la de hoy.");
            CollectionAssert.AreEqual(new[] { "BLOQUEO 1 ALC 06/10/2026", "SOLTAR" }, repositorio.Llamadas, "Se mira dentro del bloqueo y no se escribe nada");
            Assert.AreEqual(3, repositorio.Lineas.Single().Cantidad);
        }

        [TestMethod]
        public async Task Puntual_SinStockSuficienteEnLaTienda_Es400ConLaListaYNoGastaNumero()
        {
            repositorio.Stocks["41980"] = 2;
            repositorio.Stocks["36385"] = 5;

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().CrearPuntual(Peticion(("41980", 3), ("36385", 1)), Paloma));

            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            StringAssert.Contains(error.Message, "41980 (pides 3, hay 2)");
            Assert.IsFalse(error.Message.Contains("36385"));
            Assert.AreEqual("ROLLBACK", repositorio.Llamadas[repositorio.Llamadas.Count - 2]);
            Assert.IsFalse(repositorio.Llamadas.Any(l => l.StartsWith("CONTADOR")), "No se gasta número de traspaso");
            Assert.AreEqual(0, repositorio.Lineas.Count);
            Assert.AreEqual(0, registro.Cabeceras.Count);
        }

        [TestMethod]
        public async Task Puntual_SiProdExtrProductoFalla_NoQuedaNadaEnPreparacion()
        {
            repositorio.Stocks["41980"] = 5;
            repositorio.ErrorAlContabilizar = new NestoBusinessException("No se puede realizar la acción. Se quedaría un stock negativo.");

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioPuntual().CrearPuntual(Peticion(("41980", 1)), Paloma));

            Assert.AreEqual(0, repositorio.Lineas.Count, "Si algo falla no queda una reposición a medias que bloquee el job");
            Assert.AreEqual("SOLTAR", repositorio.Llamadas.Last());
        }

        [TestMethod]
        public async Task Puntual_DeOtraTiendaQueNoEsLaSuya_EsUnauthorizedSinBloqueoNiEscrituras()
        {
            // Paloma es de Alcobendas: de Reina no
            CrearReposicionDTO peticion = Peticion(("41980", 1));
            peticion.Origen = "REI";

            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => ServicioPuntual().CrearPuntual(peticion, Paloma));

            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Puntual_NoHaceFaltaEstarEnLaListaDeRellenarAMano_BastaSuTienda()
        {
            // La lista «UsuariosRellenarReposicionManual» es para la del calendario; la puntual, la tienda (Patricia, Reina)
            repositorio.Stocks["41980"] = 1;
            CrearReposicionDTO peticion = Peticion(("41980", 1));
            peticion.Origen = "REI";

            ResultadoTerminarReposicionDTO resultado = await ServicioPuntual().CrearPuntual(peticion, Usuario("NUEVAVISION\\Patricia", "Tiendas"));

            Assert.AreEqual("REI", resultado.Origen);
            Assert.AreEqual("PendRepo2", resultado.DiarioEntrada);
        }

        [DataTestMethod]
        [DataRow("ALG", "ALC", "de una tienda a Algete")]
        [DataRow("ALC", "REI", "de una tienda a Algete")]
        public async Task Puntual_SoloDeUnaTiendaAAlgete(string origen, string destino, string mensaje)
        {
            var peticion = new CrearReposicionDTO
            {
                Origen = origen, Destino = destino,
                Lineas = new List<LineaCrearReposicionDTO> { new LineaCrearReposicionDTO { Producto = "41980", Cantidad = 1 } }
            };

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().CrearPuntual(peticion, Andre));

            StringAssert.Contains(error.Message, mensaje);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Puntual_SinDestino_EsAAlgete()
        {
            repositorio.Stocks["41980"] = 1;
            var peticion = new CrearReposicionDTO
            {
                Origen = "ALC",
                Lineas = new List<LineaCrearReposicionDTO> { new LineaCrearReposicionDTO { Producto = "41980", Cantidad = 1 } }
            };

            ResultadoTerminarReposicionDTO resultado = await ServicioPuntual().CrearPuntual(peticion, Paloma);

            Assert.AreEqual("ALG", resultado.Destino);
        }

        [TestMethod]
        public async Task Puntual_SinLineasConCantidad_Es400YNoUsaLaPropuesta()
        {
            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().CrearPuntual(Peticion(("41980", 0)), Paloma));

            StringAssert.Contains(error.Message, "al menos un producto");
            Assert.AreEqual(0, propuestasPedidas.Count);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Puntual_ConInventarioEnCurso_Es409()
        {
            repositorio.InventariosEnCurso.Add("ALC");

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().CrearPuntual(Peticion(("41980", 1)), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Puntual_DespuesLaTiendaPuedeTenerLaDelCalendarioComoSiempre()
        {
            // La puntual no deja nada abierto: el job puede rellenar el siguiente viaje (no hay YaEnPreparacion)
            repositorio.Stocks["41980"] = 3;
            _ = await ServicioPuntual().CrearPuntual(Peticion(("41980", 1)), Paloma);
            propuesta.Add(new LineaPropuestaReposicionDTO { Producto = "36385", CantidadReposicion = 2 });

            ReposicionEnPreparacionDTO automatica = await ServicioPuntual().Crear(new CrearReposicionDTO
            {
                Origen = "ALC", Destino = "ALG", Herramienta = HerramientasReposicion.AUTOMATICO
            }, ServicioReposicionAutomatica.Principal(ServicioReposicionAutomatica.USUARIO_AUTOMATICO), new DateTime(2026, 10, 7, 9, 0, 0));

            Assert.AreEqual("36385", automatica.Lineas.Single().Producto);
        }

        [TestMethod]
        public void Herramienta_PuntualNoSePuedePedirPorElPostNormal()
        {
            // Solo la pone CrearPuntual: una «Puntual» en preparación no cubriría el viaje y quedaría abierta
            _ = Assert.ThrowsException<NestoBusinessException>(() => HerramientasReposicion.Resolver("Puntual", Paloma));
        }

        [TestMethod]
        public void Sql_ElJobNoDaPorCubiertoElViajeConUnaPuntual()
        {
            string sql = RepositorioReposicionAutomaticaSql.SQL_TRASPASO_QUE_CUBRE;
            StringAssert.Contains(sql, "r.Herramienta <> 'Puntual'");
            StringAssert.Contains(sql, "t.NumTraspaso = p.[NºTraspaso] AND t.Herramienta = 'Puntual'");
            StringAssert.Contains(sql, "t.NumTraspaso = e.[NºTraspaso] AND t.Herramienta = 'Puntual'");
            Assert.AreEqual(HerramientasReposicion.PUNTUAL, "Puntual");
        }

        [TestMethod]
        public async Task Controlador_Puntual_Devuelve201ConElTraspaso_SinMirarLaListaDeRellenarAMano()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            var peticion = new CrearReposicionDTO { Origen = "ALC", Lineas = new List<LineaCrearReposicionDTO>() };
            A.CallTo(() => servicio.CrearPuntual(peticion, A<IPrincipal>.Ignored)).Returns(new ResultadoTerminarReposicionDTO { NumTraspaso = 80951 });
            // La lista vacía: nadie podría rellenar a mano la del calendario, pero la puntual sí
            var controlador = new ReposicionesController(null, servicio, permisoManual: PermisoManual(string.Empty))
            {
                Request = new HttpRequestMessage(), User = Paloma
            };

            IHttpActionResult resultado = await controlador.PostPuntual(peticion);

            var creado = (NegotiatedContentResult<ResultadoTerminarReposicionDTO>)resultado;
            Assert.AreEqual(HttpStatusCode.Created, creado.StatusCode);
            Assert.AreEqual(80951, creado.Content.NumTraspaso);
        }

        [TestMethod]
        public async Task Controlador_Puntual_SinPermiso_Es403()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            A.CallTo(() => servicio.CrearPuntual(A<CrearReposicionDTO>._, A<IPrincipal>._)).Throws(new UnauthorizedAccessException("no"));
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Paloma };

            IHttpActionResult resultado = await controlador.PostPuntual(new CrearReposicionDTO { Origen = "REI" });

            Assert.AreEqual(HttpStatusCode.Forbidden, ((ResponseMessageResult)resultado).Response.StatusCode);
        }

        [TestMethod]
        public async Task ProductosPuntual_PrimeroLaReferencia_YSiNoElCodigoDeBarrasConSusUnidadesPorLectura()
        {
            var fichas = new Dictionary<string, ProductoReposicionPuntualDTO>
            {
                ["41980"] = new ProductoReposicionPuntualDTO { Producto = "41980", Nombre = "Guantes M", StockOrigen = 7 },
                ["41981"] = new ProductoReposicionPuntualDTO { Producto = "41981", Nombre = "Guantes L", StockOrigen = 2 }
            };
            var pedidas = new List<string>();
            var buscador = new ProductosReposicionPuntual(
                (empresa, origen, producto) =>
                {
                    pedidas.Add($"{empresa}|{origen}|{producto}");
                    return Task.FromResult(fichas.TryGetValue(producto, out ProductoReposicionPuntualDTO f)
                        ? new ProductoReposicionPuntualDTO { Producto = f.Producto, Nombre = f.Nombre, StockOrigen = f.StockOrigen } : null);
                },
                (empresa, codigo) => Task.FromResult(codigo == "8437017506362"
                    ? new List<NestoAPI.Infraestructure.Productos.ProductoPorCodigoBarrasDTO>
                    {
                        new NestoAPI.Infraestructure.Productos.ProductoPorCodigoBarrasDTO { Producto = "41980", Cantidad = 6, Principal = true },
                        new NestoAPI.Infraestructure.Productos.ProductoPorCodigoBarrasDTO { Producto = "41981", Cantidad = 1 }
                    }
                    : new List<NestoAPI.Infraestructure.Productos.ProductoPorCodigoBarrasDTO>()));

            List<ProductoReposicionPuntualDTO> porReferencia = await buscador.Buscar(null, " alc ", " 41980 ");
            List<ProductoReposicionPuntualDTO> porCodigo = await buscador.Buscar("1", "ALC", "8437017506362");
            List<ProductoReposicionPuntualDTO> ninguno = await buscador.Buscar("1", "ALC", "xxx");
            List<ProductoReposicionPuntualDTO> vacio = await buscador.Buscar("1", "ALC", "  ");

            Assert.AreEqual("41980", porReferencia.Single().Producto);
            Assert.AreEqual(1, porReferencia.Single().UnidadesPorLectura);
            Assert.AreEqual(7, porReferencia.Single().StockOrigen);
            Assert.AreEqual("1|ALC|41980", pedidas.First(), "Empresa por defecto y origen limpio");
            CollectionAssert.AreEqual(new[] { "41980", "41981" }, porCodigo.Select(p => p.Producto).ToArray(), "Compartido: los dos, que elija");
            Assert.AreEqual(6, porCodigo[0].UnidadesPorLectura, "El código de la caja vale 6");
            Assert.AreEqual(0, ninguno.Count);
            Assert.AreEqual(0, vacio.Count);
        }

        // ---------------------------------------------------------------- Sugerencia 581 (Carlos, 13/10/26): días con viaje, rellena, enviada y hasta la recogida

        /// <summary>
        /// El viaje de hoy en memoria. AHORA es martes 06/10/26 9:32: por defecto hay viaje de Alcobendas y de Reina a Algete
        /// los martes (cierre 9:00), el job ya lo ha rellenado y nadie ha marcado la recogida.
        /// </summary>
        private sealed class ViajesEnMemoria : IViajesReposicionPuntual
        {
            public readonly List<NestoAPI.Models.ReposicionCalendario> Calendario = new List<NestoAPI.Models.ReposicionCalendario>
            {
                Fila("ALC", 2, "09:00"), Fila("REI", 2, "09:00"), Fila("REI", 1, "09:00")
            };
            public bool HayCabecera = true;
            public bool HayColumna = true;
            public DateTime? FechaRecogida;
            public string UsuarioRecogida;
            public readonly List<string> Marcas = new List<string>();
            public readonly List<string> Lecturas = new List<string>();

            public static NestoAPI.Models.ReposicionCalendario Fila(string origen, byte dia, string cierre, bool activo = true) =>
                new NestoAPI.Models.ReposicionCalendario
                {
                    Empresa = "1  ", AlmacenOrigen = origen, AlmacenDestino = "ALG", DiaSemana = dia,
                    HoraCierre = TimeSpan.Parse(cierre), HoraLlegadaHabitual = new TimeSpan(13, 30, 0), Activo = activo
                };

            public Task<List<NestoAPI.Models.ReposicionCalendario>> LeerCalendario(string empresa) => Task.FromResult(Calendario.ToList());

            public Task<ViajeReposicionDelDia> LeerViajeDelDia(string empresa, string origen, string destino, DateTime diaCorte)
            {
                Lecturas.Add($"{empresa} {origen}→{destino} {diaCorte:dd/MM/yyyy}");
                return Task.FromResult(new ViajeReposicionDelDia
                {
                    HayCabecera = HayCabecera, HayColumnaRecogida = HayColumna, FechaRecogida = FechaRecogida, UsuarioRecogida = UsuarioRecogida
                });
            }

            public Task MarcarRecogida(string empresa, string origen, string destino, DateTime diaCorte, DateTime fecha, string usuario)
            {
                Marcas.Add($"{empresa} {origen}→{destino} {diaCorte:dd/MM/yyyy} {fecha:HH:mm} {usuario}");
                FechaRecogida = fecha;
                UsuarioRecogida = usuario;
                return Task.CompletedTask;
            }
        }

        private static CalculadoraFechaReposicion FestivoEn(string almacen) =>
            new CalculadoraFechaReposicion((dia, a) => a == almacen && dia.Date == AHORA.Date);

        private async Task<NestoBusinessException> PuntualRechazada(ServicioPreparacionReposicion servicio)
        {
            repositorio.Stocks["41980"] = 5;
            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => servicio.CrearPuntual(Peticion(("41980", 1)), Paloma));
            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            return error;
        }

        [TestMethod]
        public async Task Puntual_HoyNoHayViajeDeLaTiendaAAlgete_Es409SinBloqueoNiEscrituras()
        {
            viajes.Calendario.RemoveAll(f => f.AlmacenOrigen == "ALC");
            viajes.Calendario.Add(ViajesEnMemoria.Fila("ALC", 1, "09:00"));
            viajes.Calendario.Add(ViajesEnMemoria.Fila("ALC", 2, "09:00", activo: false));

            NestoBusinessException error = await PuntualRechazada(ServicioPuntual());

            Assert.AreEqual("Hoy no hay viaje de Alcobendas a Algete.", error.Message);
            Assert.AreEqual(0, repositorio.Llamadas.Count, "No se bloquea ni se escribe nada");
        }

        [DataTestMethod]
        [DataRow("ALC", DisplayName = "Festivo en la tienda")]
        [DataRow("ALG", DisplayName = "Festivo en Algete")]
        public async Task Puntual_DiaFestivo_NoHayViaje_Es409(string almacenFestivo)
        {
            NestoBusinessException error = await PuntualRechazada(ServicioPuntual(calculadora: FestivoEn(almacenFestivo)));

            Assert.AreEqual("Hoy no hay viaje de Alcobendas a Algete: es festivo.", error.Message);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Puntual_AntesDeLaHoraDeCierre_TodaviaNoSeHaRellenado_Es409ConLaHora()
        {
            // Se rellena a las 10:00 y son las 9:32: lo que se mande ahora podría ir en la automática
            viajes.Calendario.RemoveAll(f => f.AlmacenOrigen == "ALC");
            viajes.Calendario.Add(ViajesEnMemoria.Fila("ALC", 2, "10:00"));

            NestoBusinessException error = await PuntualRechazada(ServicioPuntual());

            Assert.AreEqual("Todavía no se ha rellenado la de hoy (se rellena sola a las 10:00).", error.Message);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Puntual_PasadoElCierrePeroElJobAunNoLaHaRellenado_Es409()
        {
            viajes.HayCabecera = false;

            NestoBusinessException error = await PuntualRechazada(ServicioPuntual());

            Assert.AreEqual("Todavía no se ha rellenado la de hoy (se rellena sola a las 09:00).", error.Message);
            CollectionAssert.AreEqual(new[] { "1 ALC→ALG 06/10/2026" }, viajes.Lecturas, "Mira la cabecera del job del día del corte");
        }

        [TestMethod]
        public async Task Puntual_ElRepartidorYaSeLaHaLlevado_Es409SinBloqueoNiEscrituras()
        {
            viajes.FechaRecogida = new DateTime(2026, 10, 6, 9, 15, 0);
            viajes.UsuarioRecogida = "NUEVAVISION\\Paloma";

            NestoBusinessException error = await PuntualRechazada(ServicioPuntual());

            Assert.AreEqual("El repartidor ya se ha llevado la reposición de hoy (a las 09:15): lo que quede va en la siguiente.", error.Message);
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Puntual_SiMarcanLaRecogidaMientrasEsperaElBloqueo_Es409SinEscribirNada()
        {
            alBloquear = () => viajes.FechaRecogida = new DateTime(2026, 10, 6, 9, 32, 0);

            NestoBusinessException error = await PuntualRechazada(ServicioPuntual());

            StringAssert.StartsWith(error.Message, "El repartidor ya se ha llevado la reposición de hoy (a las 09:32)");
            CollectionAssert.AreEqual(new[] { "BLOQUEO 1 ALC 06/10/2026", "SOLTAR" }, repositorio.Llamadas);
        }

        [TestMethod]
        public async Task Puntual_DiaConViaje_YaRellenaEnviadaYSinRecoger_SeManda()
        {
            repositorio.Stocks["41980"] = 5;

            ResultadoTerminarReposicionDTO resultado = await ServicioPuntual().CrearPuntual(Peticion(("41980", 1)), Paloma);

            Assert.IsTrue(resultado.NumTraspaso > 0);
        }

        [TestMethod]
        public async Task EstadoPuntual_CadaSituacionConSuMotivo()
        {
            EstadoReposicionPuntualDTO abierta = await ServicioPuntual().LeerEstadoPuntual(" 1 ", " alc ");
            Assert.AreEqual(SituacionesReposicionPuntual.ABIERTA, abierta.Situacion);
            Assert.IsTrue(abierta.SePuede);
            Assert.IsNull(abierta.Motivo);
            Assert.IsTrue(abierta.PuedeMarcarRecogida);
            Assert.AreEqual(new DateTime(2026, 10, 6, 9, 0, 0), abierta.CierreHoy);
            Assert.AreEqual("ALC", abierta.Origen);
            Assert.AreEqual("ALG", abierta.Destino);

            repositorio.Lineas.Add(Fila(561483500, "41980", 3));
            EstadoReposicionPuntualDTO sinEnviar = await ServicioPuntual().LeerEstadoPuntual("1", "ALC");
            Assert.AreEqual(SituacionesReposicionPuntual.SIN_ENVIAR, sinEnviar.Situacion);
            Assert.AreEqual("Primero envía la de hoy.", sinEnviar.Motivo);
            Assert.IsFalse(sinEnviar.PuedeMarcarRecogida);
            repositorio.Lineas.Clear();

            viajes.FechaRecogida = new DateTime(2026, 10, 6, 9, 20, 0);
            viajes.UsuarioRecogida = "NUEVAVISION\\Paloma";
            EstadoReposicionPuntualDTO recogida = await ServicioPuntual().LeerEstadoPuntual("1", "ALC");
            Assert.AreEqual(SituacionesReposicionPuntual.RECOGIDA, recogida.Situacion);
            Assert.IsFalse(recogida.SePuede);
            Assert.AreEqual(new DateTime(2026, 10, 6, 9, 20, 0), recogida.FechaRecogida);
            Assert.AreEqual("NUEVAVISION\\Paloma", recogida.UsuarioRecogida);
            StringAssert.Contains(recogida.Motivo, "(a las 09:20)");

            viajes.FechaRecogida = null;
            viajes.HayCabecera = false;
            EstadoReposicionPuntualDTO sinRellenar = await ServicioPuntual().LeerEstadoPuntual("1", "ALC");
            Assert.AreEqual(SituacionesReposicionPuntual.SIN_RELLENAR, sinRellenar.Situacion);

            EstadoReposicionPuntualDTO sinViaje = await ServicioPuntual(calculadora: FestivoEn("ALC")).LeerEstadoPuntual("1", "ALC");
            Assert.AreEqual(SituacionesReposicionPuntual.SIN_VIAJE, sinViaje.Situacion);
            Assert.IsNull(sinViaje.CierreHoy);
        }

        [TestMethod]
        public async Task EstadoPuntual_SinElScriptDeLaRecogida_SeHacePeroNoSeMarca()
        {
            viajes.HayColumna = false;

            EstadoReposicionPuntualDTO estado = await ServicioPuntual().LeerEstadoPuntual("1", "ALC");

            Assert.IsTrue(estado.SePuede, "Sin la columna, como hasta ahora: sin información de recogida");
            Assert.IsFalse(estado.PuedeMarcarRecogida);
        }

        // ---- Marcar la recogida

        private static MarcarRecogidaReposicionDTO Recogida(string origen = "ALC") => new MarcarRecogidaReposicionDTO { Empresa = "1", Origen = origen };

        [TestMethod]
        public async Task Recogida_ApuntaQuienYCuando_DentroDelBloqueo_YDesdeEntoncesNoHayPuntual()
        {
            EstadoReposicionPuntualDTO estado = await ServicioPuntual().MarcarRecogida(Recogida(), Paloma);

            CollectionAssert.AreEqual(new[] { "1 ALC→ALG 06/10/2026 09:32 NUEVAVISION\\Paloma" }, viajes.Marcas);
            CollectionAssert.AreEqual(new[] { "BLOQUEO 1 ALC 06/10/2026", "SOLTAR" }, repositorio.Llamadas, "El mismo bloqueo que la puntual y el job");
            Assert.AreEqual(SituacionesReposicionPuntual.RECOGIDA, estado.Situacion);
            Assert.AreEqual(AHORA, estado.FechaRecogida);
            Assert.AreEqual("NUEVAVISION\\Paloma", estado.UsuarioRecogida);
            Assert.AreEqual("El repartidor ya se ha llevado la reposición de hoy (a las 09:32): lo que quede va en la siguiente.", estado.Motivo);

            NestoBusinessException error = await PuntualRechazada(ServicioPuntual());
            Assert.AreEqual(estado.Motivo, error.Message);
        }

        [TestMethod]
        public async Task Recogida_YaMarcada_Es409SinVolverAApuntar()
        {
            viajes.FechaRecogida = new DateTime(2026, 10, 6, 9, 10, 0);

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().MarcarRecogida(Recogida(), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, "(a las 09:10)");
            Assert.AreEqual(0, viajes.Marcas.Count);
        }

        [TestMethod]
        public async Task Recogida_ConLaDeHoySinEnviar_Es409PrimeroEnviala()
        {
            repositorio.Lineas.Add(Fila(561483500, "41980", 3));

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().MarcarRecogida(Recogida(), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            Assert.AreEqual("Primero envía la de hoy.", error.Message);
            Assert.AreEqual(0, viajes.Marcas.Count);
        }

        [DataTestMethod]
        [DataRow("sinViaje", "Hoy no hay viaje de Alcobendas a Algete.")]
        [DataRow("sinRellenar", "Todavía no se ha rellenado la de hoy (se rellena sola a las 09:00).")]
        [DataRow("sinColumna", "falta lanzar Scripts/Issue581_ReposicionesTraspasos_Recogida.sql")]
        public async Task Recogida_CuandoNoToca_Es409(string caso, string mensaje)
        {
            if (caso == "sinViaje")
            {
                viajes.Calendario.RemoveAll(f => f.AlmacenOrigen == "ALC");
            }
            viajes.HayCabecera = caso != "sinRellenar";
            viajes.HayColumna = caso != "sinColumna";

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().MarcarRecogida(Recogida(), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, mensaje);
            Assert.AreEqual(0, viajes.Marcas.Count);
        }

        [TestMethod]
        public async Task Recogida_DeOtraTienda_EsUnauthorized_YAlmacenSiPuede()
        {
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => ServicioPuntual().MarcarRecogida(Recogida("REI"), Paloma));
            Assert.AreEqual(0, viajes.Marcas.Count);

            // Pensado para que más adelante lo marque el repartidor (Ariadna) con un usuario de Almacén
            EstadoReposicionPuntualDTO estado = await ServicioPuntual().MarcarRecogida(Recogida("REI"), Andre);
            Assert.AreEqual(SituacionesReposicionPuntual.RECOGIDA, estado.Situacion);
        }

        [TestMethod]
        public async Task Recogida_DesdeAlgeteOAOtraTienda_Es400()
        {
            NestoBusinessException desdeAlgete = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().MarcarRecogida(Recogida("ALG"), Andre));
            NestoBusinessException aReina = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual().MarcarRecogida(new MarcarRecogidaReposicionDTO { Origen = "ALC", Destino = "REI" }, Andre));

            Assert.AreEqual(HttpStatusCode.BadRequest, desdeAlgete.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, aReina.StatusCode);
            Assert.AreEqual(0, viajes.Marcas.Count);
        }

        [TestMethod]
        public void Sql_Recogida_NoFallaSinLasColumnas_YMarcaElViajeYLasPuntualesDeHoy()
        {
            foreach (string sql in new[] { ViajesReposicionPuntualSql.SQL_VIAJE_DEL_DIA, ViajesReposicionPuntualSql.SQL_MARCAR_RECOGIDA })
            {
                StringAssert.Contains(sql, "COL_LENGTH('dbo.ReposicionesTraspasos', 'FechaRecogida')");
                // Antes del texto dinámico las columnas solo salen dentro de COL_LENGTH
                string fuera = sql.Substring(0, sql.IndexOf("EXEC sp_executesql", StringComparison.Ordinal))
                    .Replace("'FechaRecogida')", string.Empty).Replace("'UsuarioRecogida')", string.Empty);
                Assert.IsFalse(fuera.Contains("FechaRecogida") || fuera.Contains("UsuarioRecogida"), sql);
            }
            StringAssert.Contains(ViajesReposicionPuntualSql.SQL_VIAJE_DEL_DIA, "Herramienta = 'Automatico'");
            StringAssert.Contains(ViajesReposicionPuntualSql.SQL_MARCAR_RECOGIDA, "Herramienta = ''Puntual'' AND FechaCreacion >= @hoy");
            StringAssert.Contains(ViajesReposicionPuntualSql.SQL_MARCAR_RECOGIDA, "AND FechaRecogida IS NULL");
        }

        [TestMethod]
        public void Reglas_LaRecogidaMandaSobreSinEnviar_YSinViajeNoMiraNadaMas()
        {
            var viaje = new CorteReposicion { Origen = "ALC", Destino = "ALG", Corte = new DateTime(2026, 10, 6, 9, 0, 0) };
            var recogido = new ViajeReposicionDelDia { HayCabecera = true, HayColumnaRecogida = true, FechaRecogida = new DateTime(2026, 10, 6, 9, 5, 0) };

            Assert.AreEqual(SituacionesReposicionPuntual.RECOGIDA,
                ReglasReposicionPuntual.Calcular("ALC", "ALG", null, null, viaje, false, recogido, enPreparacion: true, AHORA).Situacion);
            EstadoReposicionPuntualDTO sinViaje = ReglasReposicionPuntual.Calcular("ALC", "ALG", null, null, null, false, null, false, AHORA);
            Assert.AreEqual("Hoy no hay viaje de ALC a ALG.", sinViaje.Motivo, "Sin nombre, el código");
        }

        // ---------------------------------------------------------------- Sugerencia 581 (Carlos, 13/10/26): lo de sus pedidos no se manda

        private static IStockLibreReposicionPuntual Libres(params (string producto, int stock, int comprometido)[] productos)
        {
            IStockLibreReposicionPuntual libres = A.Fake<IStockLibreReposicionPuntual>();
            A.CallTo(() => libres.Leer(A<string>._, A<string>._, A<IEnumerable<string>>._))
                .Returns(productos.ToDictionary(p => p.producto, p => new ProductoReposicionPuntualDTO
                {
                    Producto = p.producto,
                    StockOrigen = p.stock,
                    Comprometido = p.comprometido,
                    Disponible = Math.Max(0, p.stock - p.comprometido)
                }, StringComparer.OrdinalIgnoreCase));
            return libres;
        }

        [TestMethod]
        public async Task Puntual_LoQueNecesitanSusPedidos_NoSeManda_400ConPidesYPuedesMandar()
        {
            repositorio.Stocks["41980"] = 5;
            repositorio.Stocks["36385"] = 3;
            repositorio.Stocks["17004"] = 2;
            IStockLibreReposicionPuntual libres = Libres(("41980", 5, 4), ("36385", 3, 0), ("17004", 2, 3));

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(
                () => ServicioPuntual(stockLibre: libres).CrearPuntual(Peticion(("41980", 2), ("36385", 3), ("17004", 1)), Paloma));

            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            StringAssert.Contains(error.Message, "41980 (pides 2, puedes mandar 1)");
            StringAssert.Contains(error.Message, "17004 (pides 1, puedes mandar 0)");
            Assert.IsFalse(error.Message.Contains("36385"), "De ese no tiene nada comprometido");
            StringAssert.Contains(error.Message, "sus propios pedidos pendientes");
            CollectionAssert.AreEqual(new[] { "BLOQUEO 1 ALC 06/10/2026", "SOLTAR" }, repositorio.Llamadas, "Se mira dentro del bloqueo y no se escribe nada");
            A.CallTo(() => libres.Leer("1", "ALC", A<IEnumerable<string>>.That.Matches(p => p.OrderBy(x => x).SequenceEqual(new[] { "17004", "36385", "41980" }))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Puntual_HastaLoLibre_SeManda()
        {
            repositorio.Stocks["41980"] = 5;

            ResultadoTerminarReposicionDTO resultado = await ServicioPuntual(stockLibre: Libres(("41980", 5, 4))).CrearPuntual(Peticion(("41980", 1)), Paloma);

            Assert.AreEqual(1, resultado.Unidades);
        }

        [TestMethod]
        public void Sql_FichaPuntual_ComprometidoComoLaPropuesta()
        {
            string sql = ProductosReposicionPuntual.SQL_FICHA;
            // Pedidos pendientes de la tienda (empresa y su espejo), como prdRellenarReposicionStock2
            StringAssert.Contains(sql, "l.[Almacén] = @p1 AND l.Estado BETWEEN -1 AND 1");
            StringAssert.Contains(sql, "[IVA por defecto]");
            // Salidas numeradas sin contabilizar
            StringAssert.Contains(sql, "ISNULL(p.[NºTraspaso], 0) <> 0 AND p.Cantidad < 0");
            StringAssert.Contains(sql, "AS Comprometido");
            StringAssert.Contains(sql, "ELSE 0 END AS Disponible");
        }

        [TestMethod]
        public async Task Controlador_EstadoPuntual_DevuelveLoDelServicio()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            A.CallTo(() => servicio.LeerEstadoPuntual("1", "ALC")).Returns(new EstadoReposicionPuntualDTO
            {
                Situacion = SituacionesReposicionPuntual.SIN_VIAJE, Motivo = "Hoy no hay viaje de Alcobendas a Algete."
            });
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Paloma };

            IHttpActionResult resultado = await controlador.GetEstadoPuntual("ALC");

            Assert.AreEqual(SituacionesReposicionPuntual.SIN_VIAJE, ((OkNegotiatedContentResult<EstadoReposicionPuntualDTO>)resultado).Content.Situacion);
        }

        [TestMethod]
        public async Task Controlador_Recogida_DevuelveElEstadoNuevo_Y403SinPermiso()
        {
            IServicioPreparacionReposicion servicio = A.Fake<IServicioPreparacionReposicion>();
            var deAlcobendas = new MarcarRecogidaReposicionDTO { Origen = "ALC" };
            var deReina = new MarcarRecogidaReposicionDTO { Origen = "REI" };
            A.CallTo(() => servicio.MarcarRecogida(deAlcobendas, A<IPrincipal>._)).Returns(new EstadoReposicionPuntualDTO { Situacion = SituacionesReposicionPuntual.RECOGIDA });
            A.CallTo(() => servicio.MarcarRecogida(deReina, A<IPrincipal>._)).Throws(new UnauthorizedAccessException("no"));
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Paloma };

            IHttpActionResult ok = await controlador.PostRecogida(deAlcobendas);
            IHttpActionResult prohibido = await controlador.PostRecogida(deReina);
            IHttpActionResult sinCuerpo = await controlador.PostRecogida(null);

            Assert.AreEqual(SituacionesReposicionPuntual.RECOGIDA, ((OkNegotiatedContentResult<EstadoReposicionPuntualDTO>)ok).Content.Situacion);
            Assert.AreEqual(HttpStatusCode.Forbidden, ((ResponseMessageResult)prohibido).Response.StatusCode);
            Assert.IsInstanceOfType(sinCuerpo, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Sql_FichaPuntualConStockDelOrigenYSoloActivos()
        {
            StringAssert.Contains(ProductosReposicionPuntual.SQL_FICHA, "e.[Almacén] = @p1");
            StringAssert.Contains(ProductosReposicionPuntual.SQL_FICHA, "pr.Estado >= 0");
        }

        /// <summary>La cabecera en memoria: apunta en las mismas Llamadas del repositorio para ver que va dentro de la transacción.</summary>
        private class RegistroEnMemoria : IRegistroReposicionesTraspasos
        {
            private readonly RepositorioEnMemoria repositorio;
            public readonly List<CabeceraReposicionTraspaso> Cabeceras = new List<CabeceraReposicionTraspaso>();

            public RegistroEnMemoria(RepositorioEnMemoria repositorio)
            {
                this.repositorio = repositorio;
            }

            private static string Texto(string accion, CabeceraReposicionTraspaso c) =>
                $"CABECERA {accion} {(c.NumTraspaso.HasValue ? c.NumTraspaso.ToString() : "-")} {c.Origen}→{c.Destino} {c.Herramienta} {c.UsuarioCreacion} {c.FechaCreacion:dd/MM/yyyy H:mm} " +
                $"corte {(c.FechaCorte.HasValue ? c.FechaCorte.Value.ToString("dd/MM/yyyy H:mm") : "-")}";

            public Task Crear(CabeceraReposicionTraspaso cabecera)
            {
                Cabeceras.Add(cabecera);
                repositorio.Llamadas.Add(Texto("crear", cabecera));
                return Task.CompletedTask;
            }

            public Task NumerarYMarcarPreparada(string empresa, string origen, string destino, int numTraspaso, string usuario)
            {
                repositorio.Llamadas.Add($"CABECERA numerar {origen}→{destino} {numTraspaso} {usuario}");
                return Task.CompletedTask;
            }

            public Task MarcarPreparada(string empresa, int numTraspaso, string usuario)
            {
                repositorio.Llamadas.Add($"CABECERA preparada {numTraspaso} {usuario}");
                return Task.CompletedTask;
            }

            public Task Borrar(string empresa, int numTraspaso)
            {
                repositorio.Llamadas.Add($"CABECERA borrar {numTraspaso}");
                return Task.CompletedTask;
            }
        }

        // ---------------------------------------------------------------- Repositorio en memoria

        private static FilaReposicionEnPreparacion Fila(int orden, string producto, int cantidad)
        {
            return new FilaReposicionEnPreparacion
            {
                NumeroOrden = orden, Producto = producto, Nombre = "Producto " + producto, CodigoBarras = "84" + producto,
                Cantidad = cantidad, StockOrigen = 10, Almacen = "ALG", Fecha = AHORA.Date, Usuario = "NUEVAVISION\\Paloma"
            };
        }

        /// <summary>PreExtrProducto en memoria: ALC y REI sin control de ubicaciones, ALG con él; los diarios reales.</summary>
        private class RepositorioEnMemoria : IRepositorioPreparacionReposicion
        {
            public class LineaMemoria
            {
                public FilaReposicionEnPreparacion Fila;
                public string Diario = "RepoAlcAlg";
                public int Traspaso;
                public int Cantidad => Fila.Cantidad;
            }

            public readonly List<string> Llamadas = new List<string>();
            public readonly List<FilaReposicionEnPreparacion> Lineas = new List<FilaReposicionEnPreparacion>();
            public readonly List<string> InventariosEnCurso = new List<string>();
            public readonly List<string> ProductosInexistentes = new List<string>();
            /// <summary>Sugerencia 581: el stock en el origen de lo que se inserta (lo que devuelve LeerLineasEnPreparacion). Sin él, 0.</summary>
            public readonly Dictionary<string, int> Stocks = new Dictionary<string, int>();
            public int UltimoTraspaso = 80000;
            public int LineasDeOtroAlmacen;
            public Exception ErrorAlContabilizar;
            public string TextoInsertado;

            private readonly Dictionary<string, DatosAlmacenReposicion> almacenes = new Dictionary<string, DatosAlmacenReposicion>
            {
                ["ALG"] = new DatosAlmacenReposicion { Numero = "ALG", ControlUbicaciones = true, DiarioEntradaRep = "PendRepo2", DiarioSalidaRep = "General" },
                ["REI"] = new DatosAlmacenReposicion { Numero = "REI", ControlUbicaciones = false, DiarioEntradaRep = "PendRepo", DiarioSalidaRep = "Repo2" },
                ["ALC"] = new DatosAlmacenReposicion { Numero = "ALC", ControlUbicaciones = false, DiarioEntradaRep = "RepoAlgAlc", DiarioSalidaRep = "RepoAlcAlg" }
            };
            private readonly Dictionary<FilaReposicionEnPreparacion, LineaMemoria> estado = new Dictionary<FilaReposicionEnPreparacion, LineaMemoria>();
            private List<FilaReposicionEnPreparacion> copiaAntesDeTransaccion;
            private int siguienteOrden = 900000000;

            private LineaMemoria Estado(FilaReposicionEnPreparacion fila)
            {
                if (!estado.TryGetValue(fila, out LineaMemoria linea))
                {
                    linea = new LineaMemoria { Fila = fila };
                    estado[fila] = linea;
                }
                return linea;
            }

            public IEnumerable<(string Diario, int Traspaso, int Cantidad)> Estado2 => Lineas.Select(l => (Estado(l).Diario, Estado(l).Traspaso, l.Cantidad));

            public Task<DatosAlmacenReposicion> LeerAlmacen(string empresa, string almacen)
            {
                return Task.FromResult(almacenes.TryGetValue(almacen, out DatosAlmacenReposicion datos) ? datos : null);
            }

            public Task<bool> HayInventarioEnCurso(string empresa, string almacen) => Task.FromResult(InventariosEnCurso.Contains(almacen));

            /// <summary>Almacenes.Descripción.</summary>
            public readonly Dictionary<string, string> Nombres = new Dictionary<string, string>
            {
                ["ALG"] = "Algete", ["REI"] = "Reina", ["ALC"] = "Alcobendas"
            };

            public void QuitarAlmacen(string almacen) => almacenes.Remove(almacen);

            public Task<string> LeerNombreAlmacen(string empresa, string almacen)
            {
                return Task.FromResult(almacen != null && Nombres.TryGetValue(almacen, out string nombre) ? nombre : null);
            }

            public Task<List<FilaReposicionEnPreparacion>> LeerLineasEnPreparacion(string empresa, string diario, string origen)
            {
                return Task.FromResult(Lineas.Where(l => Estado(l).Diario == diario && Estado(l).Traspaso == 0 && l.Almacen != origen).ToList());
            }

            public Task<int> ContarLineasDeOtroAlmacen(string empresa, string diario, string destino, bool soloSinTraspaso)
            {
                Llamadas.Add($"COUNT otros {diario} <>{destino}{(soloSinTraspaso ? " sin traspaso" : string.Empty)}");
                return Task.FromResult(LineasDeOtroAlmacen);
            }

            public readonly List<FilaTraspasoReposicion> Traspaso = new List<FilaTraspasoReposicion>();
            public bool Contabilizado;
            public int Lecturas;

            public Task<List<FilaTraspasoReposicion>> LeerTraspaso(string empresa, int numeroTraspaso) => Task.FromResult(Traspaso.ToList());

            public Task<bool> TraspasoContabilizado(string empresa, int numeroTraspaso) => Task.FromResult(Contabilizado);

            public Task<int> ContarLecturas(string empresa, int numeroTraspaso) => Task.FromResult(Lecturas);

            public Task<int> BorrarTraspaso(string empresa, int numeroTraspaso)
            {
                int borradas = Traspaso.Count;
                Traspaso.Clear();
                Llamadas.Add($"DELETE traspaso {numeroTraspaso} → {borradas}");
                return Task.FromResult(borradas);
            }

            public Task<int> QuitarTraspasoDePedidosEspeciales(int numeroTraspaso)
            {
                Llamadas.Add($"UPDATE pedidosespeciales quitar {numeroTraspaso}");
                return Task.FromResult(0);
            }

            public Task<int> InsertarLineaPreparacion(string empresa, string diario, string origen, string destino, string producto, int cantidad,
                DateTime fecha, string texto, string vendedor, string usuario)
            {
                Llamadas.Add($"INSERT {diario} {producto} x{cantidad} {origen}→{destino} {fecha:dd/MM/yyyy} {vendedor} {usuario}");
                TextoInsertado = texto;
                if (ProductosInexistentes.Contains(producto))
                {
                    return Task.FromResult(0);
                }
                var fila = new FilaReposicionEnPreparacion
                {
                    NumeroOrden = siguienteOrden++, Producto = producto, Nombre = "Producto " + producto, Cantidad = cantidad,
                    Almacen = destino, Fecha = fecha, Usuario = usuario,
                    StockOrigen = Stocks.TryGetValue(producto, out int stock) ? stock : 0
                };
                Lineas.Add(fila);
                Estado(fila).Diario = diario;
                return Task.FromResult(1);
            }

            public Task<int> CambiarCantidad(string empresa, string diario, int numeroOrden, int cantidad)
            {
                Llamadas.Add($"UPDATE {diario} {numeroOrden} → {cantidad}");
                FilaReposicionEnPreparacion fila = Lineas.FirstOrDefault(l => l.NumeroOrden == numeroOrden && Estado(l).Diario == diario);
                if (fila == null)
                {
                    return Task.FromResult(0);
                }
                fila.Cantidad = cantidad;
                return Task.FromResult(1);
            }

            public Task<int> BorrarLineasACero(string empresa, string diario, string destino)
            {
                int borradas = Lineas.RemoveAll(l => Estado(l).Diario == diario && l.Almacen == destino && l.Cantidad == 0);
                Llamadas.Add($"DELETE a cero {diario} {destino} → {borradas}");
                return Task.FromResult(borradas);
            }

            public Task<int> AsignarTraspaso(string empresa, string diario, string destino, int numeroTraspaso, DateTime fecha)
            {
                List<FilaReposicionEnPreparacion> afectadas = Lineas.Where(l => Estado(l).Diario == diario && l.Almacen == destino).ToList();
                afectadas.ForEach(l => Estado(l).Traspaso = numeroTraspaso);
                Llamadas.Add($"UPDATE traspaso {numeroTraspaso} fecha {fecha:dd/MM/yyyy H:mm} {diario} {destino} → {afectadas.Count}");
                return Task.FromResult(afectadas.Count);
            }

            public Task<int> ActualizarPedidosEspeciales(string empresa, string diario, string destino)
            {
                Llamadas.Add($"UPDATE pedidosespeciales {diario} {destino}");
                return Task.FromResult(0);
            }

            public Task<int> InsertarSalida(string empresa, string diario, string origen, string destino, DateTime fecha, string usuario)
            {
                int copiadas = Lineas.Count(l => Estado(l).Diario == diario && l.Almacen == destino);
                Llamadas.Add($"INSERT salida {diario} {origen} -{destino} {fecha:dd/MM/yyyy H:mm} {usuario} → {copiadas}");
                return Task.FromResult(copiadas);
            }

            public Task<int> MoverEntradaADiario(string empresa, string diario, string destino, string diarioEntrada)
            {
                List<FilaReposicionEnPreparacion> afectadas = Lineas.Where(l => Estado(l).Diario == diario && l.Almacen == destino).ToList();
                afectadas.ForEach(l => Estado(l).Diario = diarioEntrada);
                Llamadas.Add($"UPDATE entrada {diario} {destino} → {diarioEntrada} → {afectadas.Count}");
                return Task.FromResult(afectadas.Count);
            }

            public Task Contabilizar(string empresa, string diario, string usuario)
            {
                Llamadas.Add($"prdExtrProducto {empresa} {diario} {usuario}");
                return ErrorAlContabilizar != null ? Task.FromException(ErrorAlContabilizar) : Task.CompletedTask;
            }

            public Task<int> MarcarRestoEstado1(string empresa, string diario)
            {
                Llamadas.Add($"UPDATE resto estado 1 {diario}");
                return Task.FromResult(0);
            }

            public Task<int> NuevoNumeroTraspaso()
            {
                UltimoTraspaso++;
                Llamadas.Add($"CONTADOR → {UltimoTraspaso}");
                return Task.FromResult(UltimoTraspaso);
            }

            public async Task<T> EnTransaccion<T>(Func<Task<T>> trabajo)
            {
                Llamadas.Add("TRAN");
                copiaAntesDeTransaccion = Lineas.Select(Clonar).ToList();
                var estadoAntes = copiaAntesDeTransaccion.Select(c => (c.NumeroOrden, Estado(Lineas.First(l => l.NumeroOrden == c.NumeroOrden)).Diario,
                    Estado(Lineas.First(l => l.NumeroOrden == c.NumeroOrden)).Traspaso)).ToList();
                try
                {
                    T resultado = await trabajo();
                    Llamadas.Add("COMMIT");
                    return resultado;
                }
                catch
                {
                    Llamadas.Add("ROLLBACK");
                    Lineas.Clear();
                    estado.Clear();
                    foreach (FilaReposicionEnPreparacion copia in copiaAntesDeTransaccion)
                    {
                        Lineas.Add(copia);
                        var antes = estadoAntes.First(e => e.NumeroOrden == copia.NumeroOrden);
                        Estado(copia).Diario = antes.Diario;
                        Estado(copia).Traspaso = antes.Traspaso;
                    }
                    throw;
                }
            }

            private static FilaReposicionEnPreparacion Clonar(FilaReposicionEnPreparacion f)
            {
                return new FilaReposicionEnPreparacion
                {
                    NumeroOrden = f.NumeroOrden, Producto = f.Producto, Nombre = f.Nombre, CodigoBarras = f.CodigoBarras, Cantidad = f.Cantidad,
                    StockOrigen = f.StockOrigen, Almacen = f.Almacen, Fecha = f.Fecha, Usuario = f.Usuario
                };
            }
        }
    }
}
