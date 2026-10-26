using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // Ariadna#51 (corte 2, 23/10/26): reposición 81119. Paloma la escaneó entera en Nesto (lo leído, solo en memoria), el primer
    // «Terminar» falló y se terminó desde Ariadna en su móvil, donde no había nada leído: 2 de 45 y 43 diferencias. Lo leído de
    // una recepción en curso vive ahora en el servidor: se suma entre equipos y se sigue o se termina desde cualquiera.
    [TestClass]
    public class LecturasRecepcionEnCursoTests
    {
        private const string TRASPASO = "81119";

        private RepositorioLecturasEnMemoria repositorio;
        private IOrigenRecepcion reposiciones;
        private ServicioLecturasRecepcion servicio;

        private static IPrincipal Usuario(string nombre)
        {
            return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, nombre) }, "prueba"));
        }

        private static readonly IPrincipal PALOMA = Usuario("NUEVAVISION\\Paloma");
        private static readonly IPrincipal ANDRE = Usuario("Andre");

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = new RepositorioLecturasEnMemoria();
            reposiciones = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => reposiciones.Tipo).Returns("REPO");
            A.CallTo(() => reposiciones.PuedeTerminar(A<IPrincipal>.Ignored, "1", "ALC")).Returns(true);
            servicio = new ServicioLecturasRecepcion(new[] { reposiciones }, repositorio, () => new DateTime(2026, 10, 23, 12, 0, 0));
        }

        private static LecturaRecepcionEnCursoDTO Lectura(string producto, int cantidad, string dispositivo = "NESTO-RDS", Guid? id = null)
        {
            return new LecturaRecepcionEnCursoDTO
            {
                IdCliente = id ?? Guid.NewGuid(), Producto = producto, Cantidad = cantidad, Dispositivo = dispositivo,
                Fecha = new DateTime(2026, 10, 23, 11, 0, 0)
            };
        }

        private Task<LecturasRecepcionEnCursoDTO> Anotar(IPrincipal quien, params LecturaRecepcionEnCursoDTO[] lecturas)
        {
            return servicio.Anotar("REPO", "1", "ALC", TRASPASO, lecturas, quien);
        }

        private static int Cantidad(LecturasRecepcionEnCursoDTO enCurso, string producto)
        {
            return enCurso.Lecturas.SingleOrDefault(l => l.Producto == producto)?.Cantidad ?? 0;
        }

        [TestMethod]
        public async Task Get_SinNadaEnCurso_CreaElIdRecepcionYDevuelveListasVacias_YLuegoSiempreElMismo()
        {
            LecturasRecepcionEnCursoDTO primero = await servicio.Leer("REPO", "1", "alc", TRASPASO, PALOMA, "NESTO-RDS");
            LecturasRecepcionEnCursoDTO segundo = await servicio.Leer("REPO", "1", "ALC", TRASPASO, ANDRE, "PDA-03");

            Assert.AreNotEqual(Guid.Empty, primero.IdRecepcion);
            Assert.AreEqual(primero.IdRecepcion, segundo.IdRecepcion, "Nesto y Ariadna comparten el mismo IdRecepcion");
            Assert.AreEqual(0, primero.Lecturas.Count);
            Assert.AreEqual(0, primero.CodigosBulto.Count);
            Assert.AreEqual(0, primero.Equipos.Count, "Abrir no es leer: nadie ha leído nada todavía");
        }

        [TestMethod]
        public async Task Post_SumaYResta_YDevuelveLoSumado()
        {
            _ = await Anotar(PALOMA, Lectura("41899", 1), Lectura("41899", 1), Lectura("17404", 3));
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(PALOMA, Lectura("41899", -1));

            Assert.AreEqual(1, Cantidad(enCurso, "41899"));
            Assert.AreEqual(3, Cantidad(enCurso, "17404"));
            CollectionAssert.AreEqual(new[] { "41899", "17404" }, enCurso.Lecturas.Select(l => l.Producto).ToList(), "En el orden en que se leyó");
        }

        // Sugerencia 778 (reposición 81153): el EAN leído se apunta como su producto, para todos los equipos y para Nesto
        [TestMethod]
        public async Task Sugerencia778_Post_UnEan_SeApuntaComoSuProducto_YSeJuntaConLoLeidoPorReferencia()
        {
            var codigos = A.Fake<NestoAPI.Infraestructure.Productos.IRepositorioCodigosBarras>();
            A.CallTo(() => codigos.ProductosConCodigo(A<string>.Ignored, A<string>.Ignored))
                .Returns(new List<NestoAPI.Infraestructure.Productos.ProductoPorCodigoBarrasDTO>());
            A.CallTo(() => codigos.ProductosConCodigo("1", "8435454004144")).Returns(new List<NestoAPI.Infraestructure.Productos.ProductoPorCodigoBarrasDTO>
            {
                new NestoAPI.Infraestructure.Productos.ProductoPorCodigoBarrasDTO { Producto = "40945", Nombre = "MASCARILLA QUIRURGICA", Cantidad = 1, Principal = true }
            });
            A.CallTo(() => codigos.LeerProducto(A<string>.Ignored, A<string>.Ignored)).Returns(Task.FromResult<NestoAPI.Models.Producto>(null));
            servicio = new ServicioLecturasRecepcion(new[] { reposiciones }, repositorio, () => new DateTime(2026, 10, 26, 12, 0, 0), codigos);

            _ = await Anotar(ANDRE, Lectura("40945", 1, "PDA-03"));
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(ANDRE, Lectura("8435454004144", 1, "PDA-03"));

            Assert.AreEqual(2, Cantidad(enCurso, "40945"));
            Assert.AreEqual(0, Cantidad(enCurso, "8435454004144"));
            enCurso = await Anotar(ANDRE, Lectura("8435454004144", -1, "PDA-03"));
            Assert.AreEqual(1, Cantidad(enCurso, "40945"), "Quitar el EAN (una versión anterior de Ariadna) quita de su producto");
        }

        [TestMethod]
        public async Task Post_LoQuitadoDelTodo_NoSaleEnLaLista()
        {
            _ = await Anotar(PALOMA, Lectura("41899", 1));
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(PALOMA, Lectura("41899", -1));

            Assert.AreEqual(0, enCurso.Lecturas.Count);
        }

        [TestMethod]
        public async Task Post_ReenviarElMismoIdCliente_NoDuplica()
        {
            Guid id = Guid.NewGuid();
            _ = await Anotar(ANDRE, Lectura("41899", 2, "PDA-03", id));
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(ANDRE, Lectura("41899", 2, "PDA-03", id), Lectura("41899", 2, "PDA-03", id));

            Assert.AreEqual(2, Cantidad(enCurso, "41899"), "La cola de Ariadna sin red puede reenviar lo mismo");
        }

        [TestMethod]
        public void Sql_ElApunteNoSeRepitePorIdCliente_YElScriptLoGarantizaConUnIndiceUnico()
        {
            StringAssert.Contains(LecturasRecepcionEnCursoSql.SQL_ANOTAR, "WHERE NOT EXISTS (SELECT 1 FROM dbo.RecepcionesLecturasEnCurso WHERE IdCliente = @IdCliente)");
            string script = Script();
            StringAssert.Contains(script, "CREATE UNIQUE INDEX UX_RecepcionesLecturasEnCurso_IdCliente ON dbo.RecepcionesLecturasEnCurso (IdCliente);");
            StringAssert.Contains(script, "(Empresa, TipoOrigen, Documento, Almacen, Estado)");
            StringAssert.Contains(script, "GRANT SELECT, INSERT, UPDATE ON dbo.RecepcionesLecturasEnCurso TO [NUEVAVISION\\RDS2016$];");
            // #542, #294: nada de índices filtrados (CREATE … INDEX … WHERE) ni de columnas calculadas
            foreach (string linea in script.Split('\n').Where(l => l.Contains("CREATE") && l.Contains("INDEX")))
            {
                Assert.IsFalse(linea.Contains(" WHERE "), linea);
            }
            Assert.IsFalse(script.Contains(" AS ("), "Sin columnas calculadas");
        }

        [TestMethod]
        public async Task DosDispositivosALaVez_SeSuman_YCadaUnoVeAlOtro()
        {
            _ = await Anotar(PALOMA, Lectura("41899", 2, "NESTO-RDS"));
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(ANDRE, Lectura("41899", 3, "PDA-03"));
            LecturasRecepcionEnCursoDTO visto = await servicio.Leer("REPO", "1", "ALC", TRASPASO, PALOMA, "NESTO-RDS");

            Assert.AreEqual(5, Cantidad(enCurso, "41899"));
            Assert.AreEqual(5, Cantidad(visto, "41899"));
            Assert.AreEqual(2, visto.Equipos.Count);
            Assert.IsTrue(visto.Equipos.Any(e => e.Usuario == "Andre" && e.Dispositivo == "PDA-03"));
            Assert.IsTrue(visto.Equipos.Any(e => e.Usuario == "NUEVAVISION\\Paloma" && e.Dispositivo == "NESTO-RDS"));
        }

        [TestMethod]
        public async Task Post_EtiquetasDeBulto_SinRepetirYSinLasQuitadas()
        {
            _ = await Anotar(PALOMA, new LecturaRecepcionEnCursoDTO { IdCliente = Guid.NewGuid(), CodigoBulto = "r81119'1", Cantidad = 1 },
                new LecturaRecepcionEnCursoDTO { IdCliente = Guid.NewGuid(), CodigoBulto = "R81119-2", Cantidad = 1 });
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(PALOMA,
                new LecturaRecepcionEnCursoDTO { IdCliente = Guid.NewGuid(), CodigoBulto = "R81119-2", Cantidad = -1 });

            CollectionAssert.AreEqual(new[] { "R81119-1" }, enCurso.CodigosBulto, "Normalizada como al terminar (NestoAPI#675)");
            Assert.AreEqual(0, enCurso.Lecturas.Count);
        }

        [TestMethod]
        public async Task Post_SinIdCliente_NoApuntaNada()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Anotar(PALOMA, Lectura("41899", 1), new LecturaRecepcionEnCursoDTO { Producto = "17404", Cantidad = 1 }));

            StringAssert.Contains(ex.Message, "IdCliente");
            Assert.AreEqual(0, repositorio.Filas.Count(f => f.Producto != null));
        }

        [TestMethod]
        public async Task Delete_DescartaLoLeido_YDaUnIdRecepcionNuevo()
        {
            LecturasRecepcionEnCursoDTO antes = await Anotar(PALOMA, Lectura("41899", 2));

            LecturasRecepcionEnCursoDTO despues = await servicio.Descartar("REPO", "1", "ALC", TRASPASO, PALOMA, "NESTO-RDS");
            LecturasRecepcionEnCursoDTO otraVez = await servicio.Leer("REPO", "1", "ALC", TRASPASO, ANDRE, "PDA-03");

            Assert.AreNotEqual(antes.IdRecepcion, despues.IdRecepcion);
            Assert.AreEqual(despues.IdRecepcion, otraVez.IdRecepcion);
            Assert.AreEqual(0, despues.Lecturas.Count);
            Assert.AreEqual(0, otraVez.Lecturas.Count);
            Assert.IsTrue(repositorio.Filas.Where(f => f.IdRecepcion == antes.IdRecepcion).All(f => f.Estado == LecturasRecepcionEnCursoSql.DESCARTADA),
                "No se borra nada: queda descartado");
        }

        [TestMethod]
        public async Task SinTabla_DevuelveNull_YNoTocaNada()
        {
            repositorio.Existe = false;

            Assert.IsNull(await servicio.Leer("REPO", "1", "ALC", TRASPASO, PALOMA, null));
            Assert.IsNull(await Anotar(PALOMA, Lectura("41899", 1)));
            Assert.IsNull(await servicio.Descartar("REPO", "1", "ALC", TRASPASO, PALOMA, null));
            Assert.AreEqual(0, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task SinPermisoParaRecibirEnEseAlmacen_NoLeeNiApunta()
        {
            A.CallTo(() => reposiciones.PuedeTerminar(A<IPrincipal>.Ignored, "1", "REI")).Returns(false);

            var ex = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => servicio.Leer("REPO", "1", "REI", TRASPASO, PALOMA, null));
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                servicio.Anotar("REPO", "1", "REI", TRASPASO, new[] { Lectura("41899", 1) }, PALOMA));
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => servicio.Descartar("REPO", "1", "REI", TRASPASO, PALOMA, null));

            StringAssert.Contains(ex.Message, "No tiene permiso para recibir reposiciones en REI");
            Assert.AreEqual(0, repositorio.Filas.Count);
        }

        [TestMethod]
        public async Task SinPermiso_Es403AunqueNoHayaTabla()
        {
            repositorio.Existe = false;
            A.CallTo(() => reposiciones.PuedeTerminar(A<IPrincipal>.Ignored, "1", "REI")).Returns(false);

            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => servicio.Leer("REPO", "1", "REI", TRASPASO, PALOMA, null));
        }

        [TestMethod]
        public async Task SinIdentificar_NoLee()
        {
            _ = await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                servicio.Leer("REPO", "1", "ALC", TRASPASO, new ClaimsPrincipal(new ClaimsIdentity()), null));
        }

        [TestMethod]
        public void Grupo_DeLaRecepcion_EsElDelContrato()
        {
            Assert.AreEqual("recepcion:REPO:81119:ALC", AvisosAlmacen.GrupoRecepcion("repo", " 81119 ", "alc"));
            Assert.AreEqual("recepcion:REPO:81119:ALC", AvisosAlmacen.Normalizar("Recepcion:repo:81119:alc"), "El hub admite el grupo (Entrar)");
            Assert.AreEqual("recepcion:COMP:65:ALG", AvisosAlmacen.Normalizar("recepcion:COMP:65:ALG"));
            Assert.IsNull(AvisosAlmacen.Normalizar("recepcion:REPO:81119"));
            Assert.AreEqual("Recogida:PICK:99633", AvisosAlmacen.Normalizar("recogida:pick:99633"), "Los de antes, igual");
        }

        // --- Terminar: en la misma transacción que la recepción ---

        [TestMethod]
        public async Task Terminar_ConElIdRecepcionCompartido_LoLeidoPasaATerminada()
        {
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(PALOMA, Lectura("A", 2), Lectura("B", 3));
            ITransaccionCierreReposicion transaccion = TransaccionReposicion(("A", 2), ("B", 3));

            ResultadoTerminarRecepcionDTO resultado = await Reposicion(transaccion).Terminar(SolicitudReposicion(enCurso.IdRecepcion, false, ("A", 2), ("B", 3)));

            Assert.IsFalse(resultado.Parcial);
            A.CallTo(() => transaccion.TerminarLecturasEnCurso(
                A<ClaveRecepcionEnCurso>.That.Matches(c => c.Empresa == "1" && c.Tipo == "REPO" && c.Documento == TRASPASO && c.Almacen == "ALC"),
                enCurso.IdRecepcion, false, "NUEVAVISION\\Paloma", "NESTO-RDS")).MustHaveHappenedOnceExactly();
            Assert.IsTrue(repositorio.Filas.Where(f => f.IdRecepcion == enCurso.IdRecepcion).All(f => f.Estado == LecturasRecepcionEnCursoSql.TERMINADA));
            LecturasRecepcionEnCursoDTO despues = await servicio.Leer("REPO", "1", "ALC", TRASPASO, ANDRE, null);
            Assert.AreNotEqual(enCurso.IdRecepcion, despues.IdRecepcion, "Lo de antes ya no está en curso");
            Assert.AreEqual(0, despues.Lecturas.Count);
        }

        [TestMethod]
        public async Task Terminar_EnParte_AbreUnIdRecepcionNuevoParaLoQueQueda()
        {
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(PALOMA, Lectura("A", 1));
            ITransaccionCierreReposicion transaccion = TransaccionReposicion(("A", 2), ("B", 3));

            ResultadoTerminarRecepcionDTO resultado = await Reposicion(transaccion).Terminar(SolicitudReposicion(enCurso.IdRecepcion, true, ("A", 1)));

            Assert.IsTrue(resultado.Parcial);
            A.CallTo(() => transaccion.TerminarLecturasEnCurso(A<ClaveRecepcionEnCurso>.Ignored, enCurso.IdRecepcion, true, A<string>.Ignored, A<string>.Ignored))
                .MustHaveHappenedOnceExactly();
            Guid nuevo = repositorio.Filas.Where(f => f.Estado == LecturasRecepcionEnCursoSql.EN_CURSO).Select(f => f.IdRecepcion).Distinct().Single();
            Assert.AreNotEqual(enCurso.IdRecepcion, nuevo);
            LecturasRecepcionEnCursoDTO despues = await servicio.Leer("REPO", "1", "ALC", TRASPASO, ANDRE, null);
            Assert.AreEqual(nuevo, despues.IdRecepcion, "Lo que queda se sigue leyendo con el IdRecepcion nuevo");
            Assert.AreEqual(0, despues.Lecturas.Count);
        }

        [TestMethod]
        public async Task Terminar_ConOtroIdRecepcion_NoTocaLoEnCurso()
        {
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(PALOMA, Lectura("A", 2), Lectura("B", 3));
            ITransaccionCierreReposicion transaccion = TransaccionReposicion(("A", 2), ("B", 3));

            _ = await Reposicion(transaccion).Terminar(SolicitudReposicion(Guid.NewGuid(), false, ("A", 2), ("B", 3)));

            Assert.IsTrue(repositorio.Filas.All(f => f.Estado == LecturasRecepcionEnCursoSql.EN_CURSO), "Una versión anterior manda su propio IdRecepcion");
            Assert.AreEqual(enCurso.IdRecepcion, (await servicio.Leer("REPO", "1", "ALC", TRASPASO, ANDRE, null)).IdRecepcion);
        }

        [TestMethod]
        public async Task Terminar_SiFallaLaRecepcion_NoSeMarcaNada()
        {
            LecturasRecepcionEnCursoDTO enCurso = await Anotar(PALOMA, Lectura("A", 2));
            ITransaccionCierreReposicion transaccion = TransaccionReposicion(("A", 2));
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).Throws(new Exception("prdExtrProducto"));

            _ = await Assert.ThrowsExceptionAsync<Exception>(() => Reposicion(transaccion).Terminar(SolicitudReposicion(enCurso.IdRecepcion, false, ("A", 2))));

            A.CallTo(() => transaccion.TerminarLecturasEnCurso(A<ClaveRecepcionEnCurso>.Ignored, A<Guid>.Ignored, A<bool>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
            Assert.IsTrue(repositorio.Filas.All(f => f.Estado == LecturasRecepcionEnCursoSql.EN_CURSO), "Lo leído sigue ahí para reintentar desde cualquier equipo");
        }

        [TestMethod]
        public async Task Terminar_Compras_MarcaTerminadaSinAbrirOtra()
        {
            var repositorioCompras = A.Fake<IRepositorioRecepcionCompras>();
            var transaccion = A.Fake<ITransaccionRecepcionCompra>();
            A.CallTo(() => repositorioCompras.EnTransaccion(A<Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, A<bool>.Ignored))
                .ReturnsLazily((Func<ITransaccionRecepcionCompra, Task<ResultadoTerminarRecepcionDTO>> trabajo, bool deshacer) => trabajo(transaccion));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.LeerLineasBloqueando("1", "ALG", "65")).Returns(new List<LineaCompraPendiente>
            {
                new LineaCompraPendiente { Pedido = 900, FechaPedido = new DateTime(2026, 9, 1), NumeroOrden = 1, Producto = "A", Cantidad = 2,
                    FechaRecepcion = new DateTime(2026, 10, 22), VistoBueno = true, ControlPendientes = true }
            });
            Guid id = Guid.NewGuid();
            IPrincipal almacen = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre"), new Claim(ClaimTypes.Role, "NUEVAVISION\\Almacén") }, "prueba"));

            _ = await new OrigenRecepcionCompras(repositorioCompras, A.Fake<IAvisadorCompras>(), () => new DateTime(2026, 10, 23)).Terminar(new SolicitudTerminarRecepcion
            {
                Empresa = "1", Almacen = "ALG", Documento = "65", IdRecepcion = id, Lecturas = new Dictionary<string, int> { ["A"] = 2 },
                Usuario = "Andre", Principal = almacen, Dispositivo = "PDA-03"
            });

            A.CallTo(() => transaccion.TerminarLecturasEnCurso(
                A<ClaveRecepcionEnCurso>.That.Matches(c => c.Tipo == "COMP" && c.Documento == "65" && c.Almacen == "ALG"), id, false, "Andre", "PDA-03"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Sql_Terminar_SoloElIdRecepcionCompartidoYSinTablaNoHaceNada()
        {
            string sql = LecturasRecepcionEnCursoSql.SQL_TERMINAR;
            StringAssert.Contains(sql, "IF OBJECT_ID('dbo.RecepcionesLecturasEnCurso', 'U') IS NOT NULL");
            StringAssert.Contains(sql, "AND IdRecepcion = @IdRecepcion AND Estado = 'EnCurso'");
            StringAssert.Contains(sql, "@Terminadas > 0 AND @AbrirOtra = 1");
        }

        // --- Ayudas ---

        private ITransaccionCierreReposicion TransaccionReposicion(params (string producto, int cantidad)[] enviado)
        {
            var transaccion = A.Fake<ITransaccionCierreReposicion>();
            int traspaso = int.Parse(TRASPASO);
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.DiarioDeEntrada("1", "ALC")).Returns("PendRepo");
            A.CallTo(() => transaccion.TraspasosEnDiario("1", "ALC", "PendRepo")).Returns(new List<int> { traspaso });
            A.CallTo(() => transaccion.LeerBultos("1", traspaso)).Returns(new List<FilaBultoReposicion>());
            A.CallTo(() => transaccion.LeerLineas("1", "ALC", traspaso))
                .Returns(enviado.Select(l => new FilaReposicion { Producto = l.producto, Cantidad = l.cantidad }).ToList());
            A.CallTo(() => transaccion.ApartarOtros("1", "PendRepo", traspaso)).Returns(new List<int>());
            A.CallTo(() => transaccion.ApartarNoLeido("1", "ALC", "PendRepo", traspaso, A<IReadOnlyDictionary<string, int>>.Ignored)).Returns(new List<int>());
            // Lo mismo que hace el SQL de verdad (LecturasRecepcionEnCursoSql.SQL_TERMINAR), sobre las filas en memoria
            A.CallTo(() => transaccion.TerminarLecturasEnCurso(A<ClaveRecepcionEnCurso>.Ignored, A<Guid>.Ignored, A<bool>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .ReturnsLazily((ClaveRecepcionEnCurso clave, Guid id, bool abrirOtra, string usuario, string dispositivo) =>
                    Task.FromResult(repositorio.Terminar(clave, id, abrirOtra, usuario, dispositivo)));
            return transaccion;
        }

        private static OrigenRecepcionReposiciones Reposicion(ITransaccionCierreReposicion transaccion)
        {
            var cierre = A.Fake<IRepositorioCierreReposiciones>();
            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, A<bool>.Ignored))
                .ReturnsLazily((Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> t, bool deshacer) => t(transaccion));
            return new OrigenRecepcionReposiciones(A.Fake<IServicioRecepcionReposiciones>(), cierre, A.Fake<IAvisadorReposiciones>(), (e, u) => "ALC",
                A.Fake<NestoAPI.Infraestructure.Reposiciones.IServicioDiferenciasReposicion>(), A.Fake<IRepositorioBultosReposicion>());
        }

        private static SolicitudTerminarRecepcion SolicitudReposicion(Guid idRecepcion, bool parcial, params (string producto, int cantidad)[] lecturas)
        {
            return new SolicitudTerminarRecepcion
            {
                Empresa = "1", Almacen = "ALC", Documento = TRASPASO, IdRecepcion = idRecepcion,
                Lecturas = lecturas.ToDictionary(l => l.producto, l => l.cantidad),
                Usuario = "NUEVAVISION\\Paloma", Principal = PALOMA, Parcial = parcial, Dispositivo = "NESTO-RDS"
            };
        }

        private static string Script()
        {
            string carpeta = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && carpeta != null; i++)
            {
                string ruta = Path.Combine(carpeta, "Scripts", "Ariadna51_RecepcionesLecturasEnCurso.sql");
                if (File.Exists(ruta))
                {
                    return File.ReadAllText(ruta);
                }
                carpeta = Path.GetDirectoryName(carpeta.TrimEnd(Path.DirectorySeparatorChar));
            }
            Assert.Inconclusive("No se encuentra Scripts/Ariadna51_RecepcionesLecturasEnCurso.sql");
            return null;
        }

        /// <summary>
        /// La tabla en memoria, con las mismas reglas que el SQL: un IdRecepcion en curso por recepción (lo crea quien abre), un
        /// IdCliente no se repite nunca, descartar marca y abre otro, terminar marca solo las del IdRecepcion que llega.
        /// </summary>
        private sealed class RepositorioLecturasEnMemoria : IRepositorioLecturasRecepcion
        {
            internal sealed class Fila
            {
                public long Id;
                public Guid IdCliente;
                public ClaveRecepcionEnCurso Clave;
                public Guid IdRecepcion;
                public string Producto;
                public string CodigoBulto;
                public int Cantidad;
                public string Usuario;
                public string Dispositivo;
                public DateTime Fecha;
                public string Estado;
            }

            public bool Existe { get; set; } = true;
            public List<Fila> Filas { get; } = new List<Fila>();
            private long siguiente = 1;

            public bool TablaExiste() => Existe;

            private static bool Es(Fila f, ClaveRecepcionEnCurso c) =>
                f.Clave.Empresa == c.Empresa && f.Clave.Tipo == c.Tipo && f.Clave.Documento == c.Documento && f.Clave.Almacen == c.Almacen;

            private Guid AbrirSinLeer(ClaveRecepcionEnCurso clave, string usuario, string dispositivo)
            {
                Fila enCurso = Filas.Where(f => Es(f, clave) && f.Estado == LecturasRecepcionEnCursoSql.EN_CURSO).OrderBy(f => f.Id).FirstOrDefault();
                if (enCurso != null)
                {
                    return enCurso.IdRecepcion;
                }
                Guid id = Guid.NewGuid();
                Filas.Add(new Fila
                {
                    Id = siguiente++, IdCliente = Guid.NewGuid(), Clave = clave, IdRecepcion = id, Usuario = usuario, Dispositivo = dispositivo,
                    Fecha = DateTime.Now, Estado = LecturasRecepcionEnCursoSql.EN_CURSO
                });
                return id;
            }

            private List<FilaLecturaRecepcionEnCurso> Leer(ClaveRecepcionEnCurso clave, Guid id) => Filas
                .Where(f => Es(f, clave) && f.Estado == LecturasRecepcionEnCursoSql.EN_CURSO && f.IdRecepcion == id)
                .Select(f => new FilaLecturaRecepcionEnCurso
                {
                    Id = f.Id, IdRecepcion = f.IdRecepcion, Producto = f.Producto, CodigoBulto = f.CodigoBulto, Cantidad = f.Cantidad,
                    Usuario = f.Usuario, Dispositivo = f.Dispositivo, Fecha = f.Fecha
                }).ToList();

            public Task<List<FilaLecturaRecepcionEnCurso>> Abrir(ClaveRecepcionEnCurso clave, string usuario, string dispositivo)
            {
                return Task.FromResult(Leer(clave, AbrirSinLeer(clave, usuario, dispositivo)));
            }

            public Task<List<FilaLecturaRecepcionEnCurso>> Anotar(ClaveRecepcionEnCurso clave, string usuario, IReadOnlyCollection<LecturaRecepcionEnCursoNueva> lecturas)
            {
                Guid id = AbrirSinLeer(clave, usuario, lecturas.Select(l => l.Dispositivo).FirstOrDefault());
                foreach (LecturaRecepcionEnCursoNueva l in lecturas)
                {
                    if (Filas.Any(f => f.IdCliente == l.IdCliente))
                    {
                        continue;
                    }
                    Filas.Add(new Fila
                    {
                        Id = siguiente++, IdCliente = l.IdCliente, Clave = clave, IdRecepcion = id, Producto = l.Producto, CodigoBulto = l.CodigoBulto,
                        Cantidad = l.Cantidad, Usuario = usuario, Dispositivo = l.Dispositivo, Fecha = l.Fecha, Estado = LecturasRecepcionEnCursoSql.EN_CURSO
                    });
                }
                return Task.FromResult(Leer(clave, id));
            }

            public Task<List<FilaLecturaRecepcionEnCurso>> Descartar(ClaveRecepcionEnCurso clave, string usuario, string dispositivo)
            {
                foreach (Fila f in Filas.Where(f => Es(f, clave) && f.Estado == LecturasRecepcionEnCursoSql.EN_CURSO))
                {
                    f.Estado = LecturasRecepcionEnCursoSql.DESCARTADA;
                }
                return Abrir(clave, usuario, dispositivo);
            }

            public int Terminar(ClaveRecepcionEnCurso clave, Guid id, bool abrirOtra, string usuario, string dispositivo)
            {
                List<Fila> suyas = Filas.Where(f => Es(f, clave) && f.IdRecepcion == id && f.Estado == LecturasRecepcionEnCursoSql.EN_CURSO).ToList();
                suyas.ForEach(f => f.Estado = LecturasRecepcionEnCursoSql.TERMINADA);
                if (suyas.Count > 0 && abrirOtra)
                {
                    _ = AbrirSinLeer(clave, usuario, dispositivo);
                }
                return suyas.Count;
            }
        }
    }
}
