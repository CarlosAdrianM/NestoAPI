using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
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
        private Dictionary<string, string> almacenesDeUsuario;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = new RepositorioEnMemoria();
            propuestasPedidas = new List<string>();
            propuesta = new List<LineaPropuestaReposicionDTO>();
            huecos = A.Fake<IUbicacionesReposicion>();
            almacenesDeUsuario = new Dictionary<string, string> { ["Paloma"] = "ALC", ["Patricia"] = "REI" };
        }

        private ServicioPreparacionReposicion Servicio()
        {
            return new ServicioPreparacionReposicion(repositorio,
                (empresa, origen, destino) => { propuestasPedidas.Add($"{empresa}|{origen}|{destino}"); return Task.FromResult(propuesta); },
                (origen, control) => control ? throw new NestoBusinessException(string.Format(UbicacionesReposicion.MENSAJE_NO_DISPONIBLE, origen)) : huecos,
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
            A.CallTo(() => huecos.ReservarAlImprimir("1", "RepoAlcAlg", "ALG")).MustHaveHappenedOnceExactly();
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

            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Crear(Peticion(("41980", 5)), Paloma));

            Assert.AreEqual(HttpStatusCode.Conflict, error.StatusCode);
            StringAssert.Contains(error.Message, "Ya hay una reposición en preparación de ALC a ALG");
            Assert.AreEqual(0, repositorio.Llamadas.Count);
        }

        [TestMethod]
        public async Task Crear_DesdeAlmacenConControlDeUbicaciones_Es400HastaLaPuertaUnicaDeUbicaciones()
        {
            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Crear(new CrearReposicionDTO { Origen = "ALG", Destino = "ALC", Lineas = Peticion(("41980", 5)).Lineas },
                    Usuario("NUEVAVISION\\Andre", "Almacén")));

            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            StringAssert.Contains(error.Message, "Nesto viejo");
            StringAssert.Contains(error.Message, "#594");
            Assert.AreEqual(0, repositorio.Llamadas.Count);
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
            A.CallTo(() => huecos.DevolverSobrante("1", 561483500, 3)).Invokes(() => repositorio.Llamadas.Add("HUECOS devolver 561483500 → 3"));

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
            A.CallTo(() => huecos.DescontarAlTerminar("1", "RepoAlcAlg", "ALG", 80893)).Invokes(() => repositorio.Llamadas.Add("HUECOS descontar 80893"));

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
            var controlador = new ReposicionesController(null, servicio) { Request = new HttpRequestMessage(), User = Paloma };

            IHttpActionResult resultado = await controlador.PostCrear(peticion);

            var creado = (NegotiatedContentResult<ReposicionEnPreparacionDTO>)resultado;
            Assert.AreEqual(HttpStatusCode.Created, creado.StatusCode);
            Assert.AreEqual("ALG", creado.Content.Destino);
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

            public Task<List<FilaReposicionEnPreparacion>> LeerLineasEnPreparacion(string empresa, string diario, string origen)
            {
                return Task.FromResult(Lineas.Where(l => Estado(l).Diario == diario && Estado(l).Traspaso == 0 && l.Almacen != origen).ToList());
            }

            public Task<int> ContarLineasDeOtroAlmacen(string empresa, string diario, string destino)
            {
                Llamadas.Add($"COUNT otros {diario} <>{destino}");
                return Task.FromResult(LineasDeOtroAlmacen);
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
                    Almacen = destino, Fecha = fecha, Usuario = usuario
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
