using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    // NestoAPI#695 (novedades 751 y 788). Al enviar una reposición, las entradas del destino se crean en PreExtrProducto con la
    // fecha del ENVÍO, y trgExtractoProductoUpd (FOR INSERT, UPDATE) rechaza cualquier apunte con Fecha anterior a un inventario
    // (Inventarios.Estado >= 2) del mismo producto y almacén. La 81028 (Reina → Algete, enviada el 16/10) se atascó por el
    // inventario del 43992 del 19/10; la 81177 (Algete → Alcobendas, enviada el 26/10 a las 13:00) por el del 42267 de las 14:19.
    // Decisión de Carlos (27/10/26), opción (a): la ENTRADA se apunta SIEMPRE con la fecha de la recepción; la salida del origen
    // se queda con la del envío. Y se avisa de los productos con un inventario posterior al envío, por si ya se contaron.
    [TestClass]
    public class FechaEntradaReposicionTests
    {
        private const int TRASPASO = 81177;

        private IRepositorioCierreReposiciones cierre;
        private ITransaccionCierreReposicion transaccion;
        private IServicioRecepcionReposiciones servicio;
        private List<string> pasos;

        [TestInitialize]
        public void Inicializar()
        {
            pasos = new List<string>();
            cierre = A.Fake<IRepositorioCierreReposiciones>();
            transaccion = A.Fake<ITransaccionCierreReposicion>();
            servicio = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => cierre.EnTransaccion(A<Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>>>.Ignored, A<bool>.Ignored))
                .ReturnsLazily((Func<ITransaccionCierreReposicion, Task<ResultadoTerminarRecepcionDTO>> t, bool deshacer) => t(transaccion));
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(false);
            A.CallTo(() => transaccion.DiarioDeEntrada("1", "ALC")).Returns("RepoAlgAlc");
            A.CallTo(() => transaccion.TraspasosEnDiario("1", "ALC", "RepoAlgAlc")).Returns(new List<int> { TRASPASO });
            A.CallTo(() => transaccion.LeerBultos("1", TRASPASO)).Returns(new List<FilaBultoReposicion>());
            A.CallTo(() => transaccion.LeerDatosTraspaso("1", "ALC", "RepoAlgAlc", TRASPASO))
                .Returns(new DatosTraspasoReposicion { Origen = "ALG", Creador = "NUEVAVISION\\Andre" });
            A.CallTo(() => transaccion.LeerLineas("1", "ALC", TRASPASO)).Returns(new List<FilaReposicion>
            {
                new FilaReposicion { Producto = "42267", Descripcion = "VELVET LIPSTICK", Cantidad = 1 },
                new FilaReposicion { Producto = "45131", Descripcion = "Otro", Cantidad = 1 }
            });
            A.CallTo(() => transaccion.ApartarOtros("1", "RepoAlgAlc", TRASPASO)).Invokes(() => pasos.Add("apartar otros")).Returns(new List<int>());
            A.CallTo(() => transaccion.ApartarNoLeido("1", "ALC", "RepoAlgAlc", TRASPASO, A<IReadOnlyDictionary<string, int>>.Ignored))
                .Invokes(() => pasos.Add("apartar no leído")).Returns(new List<int> { 900 });
            A.CallTo(() => transaccion.AjustarALoLeido(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<int>.Ignored, A<string>.Ignored, A<int>.Ignored))
                .Invokes((string e, string a, string d, int t, string p, int c) => pasos.Add($"ajustar {p} a {c}"));
            A.CallTo(() => transaccion.LeerInventariosTrasEnvio("1", "ALC", TRASPASO)).Invokes(() => pasos.Add("leer inventarios"))
                .Returns(new List<InventarioTrasEnvio>());
            A.CallTo(() => transaccion.FecharEntradaAhora("1", "ALC", "RepoAlgAlc", TRASPASO)).Invokes(() => pasos.Add("fechar entrada")).Returns(2);
            A.CallTo(() => transaccion.LeerPendientesDeUbicar("1", "RepoAlgAlc")).Invokes(() => pasos.Add("leer pendientes de ubicar"));
            A.CallTo(() => transaccion.Contabilizar("1", "RepoAlgAlc", A<string>.Ignored)).Invokes(() => pasos.Add("contabilizar"));
            A.CallTo(() => transaccion.DevolverApartadas("1", "RepoAlgAlc", A<IReadOnlyCollection<int>>.Ignored)).Invokes(() => pasos.Add("devolver"));
        }

        private OrigenRecepcionReposiciones Reposiciones()
        {
            return new OrigenRecepcionReposiciones(servicio, cierre, A.Fake<IAvisadorReposiciones>(), (e, u) => "ALC",
                A.Fake<NestoAPI.Infraestructure.Reposiciones.IServicioDiferenciasReposicion>(), A.Fake<IRepositorioBultosReposicion>());
        }

        private static SolicitudTerminarRecepcion Solicitud(bool parcial, params (string producto, int cantidad)[] lecturas)
        {
            return new SolicitudTerminarRecepcion
            {
                Empresa = "1", Almacen = "ALC", Documento = TRASPASO.ToString(), IdRecepcion = Guid.NewGuid(),
                Lecturas = lecturas.ToDictionary(l => l.producto, l => l.cantidad),
                Usuario = "NUEVAVISION\\Paloma", Parcial = parcial,
                Principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Paloma") }, "prueba"))
            };
        }

        private static InventarioTrasEnvio Inventario42267()
        {
            return new InventarioTrasEnvio
            {
                Producto = "42267",
                FechaInventario = new DateTime(2026, 10, 26, 14, 19, 36),
                Regularizado = 1,
                Usuario = "NUEVAVISION\\Paloma",
                FechaEnvio = new DateTime(2026, 10, 26, 13, 0, 16)
            };
        }

        // ------------------------------------------------------------------ la decisión: la entrada se fecha al recibir, siempre

        [TestMethod]
        public async Task Terminar_Entera_FechaLaEntradaConLaRecepcionJustoAntesDeContabilizar()
        {
            _ = await Reposiciones().Terminar(Solicitud(false, ("42267", 1), ("45131", 1)));

            // Después de ajustar el diario a lo leído y de mirar los inventarios (con la fecha del envío todavía), antes del procedimiento
            CollectionAssert.AreEqual(new[] { "apartar otros", "leer pendientes de ubicar", "leer inventarios", "fechar entrada", "contabilizar", "devolver" }, pasos);
            A.CallTo(() => transaccion.FecharEntradaAhora("1", "ALC", "RepoAlgAlc", TRASPASO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Terminar_EnParte_TambienFechaLaEntradaConLaRecepcion()
        {
            // «Dar de alta solo lo leído» (81177 se desatascó así): lo que entra se fecha; lo apartado (no leído) se queda con la del envío
            _ = await Reposiciones().Terminar(Solicitud(true, ("42267", 1)));

            int fechar = pasos.IndexOf("fechar entrada");
            Assert.IsTrue(fechar > pasos.IndexOf("apartar no leído"), "se fecha después de apartar lo no leído: " + string.Join(" > ", pasos));
            Assert.IsTrue(fechar > pasos.IndexOf("leer inventarios"), "los inventarios se miran con la fecha del envío todavía: " + string.Join(" > ", pasos));
            Assert.AreEqual(fechar + 1, pasos.IndexOf("contabilizar"), "se contabiliza justo después: " + string.Join(" > ", pasos));
        }

        [TestMethod]
        public async Task Terminar_ConDiferencias_FechaDespuesDeAjustarALoLeido()
        {
            // Lo que no venía se añade al diario copiando la fecha del envío: también se fecha al recibir
            _ = await Reposiciones().Terminar(Solicitud(false, ("42267", 1), ("45131", 1), ("99999", 2)));

            Assert.IsTrue(pasos.IndexOf("fechar entrada") > pasos.IndexOf("ajustar 99999 a 2"), string.Join(" > ", pasos));
            Assert.IsTrue(pasos.IndexOf("fechar entrada") < pasos.IndexOf("contabilizar"), string.Join(" > ", pasos));
        }

        [TestMethod]
        public async Task Terminar_SinNadaLeido_NoFechaNiContabiliza()
        {
            // Novedad 751: lo que quedaba tras recibir en parte no ha llegado: no entra nada, nada que fechar
            _ = await Reposiciones().Terminar(Solicitud(false, ("42267", 0), ("45131", 0)));

            A.CallTo(() => transaccion.FecharEntradaAhora(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<int>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => transaccion.LeerInventariosTrasEnvio(A<string>.Ignored, A<string>.Ignored, A<int>.Ignored)).MustNotHaveHappened();
            A.CallTo(() => transaccion.Contabilizar(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Terminar_YaTerminada_NoFechaNada()
        {
            A.CallTo(() => transaccion.YaRegistrada(A<string>.Ignored, A<IEnumerable<Guid>>.Ignored)).Returns(true);

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones().Terminar(Solicitud(false, ("42267", 1), ("45131", 1)));

            Assert.IsTrue(resultado.YaEstabaTerminada);
            A.CallTo(() => transaccion.FecharEntradaAhora(A<string>.Ignored, A<string>.Ignored, A<string>.Ignored, A<int>.Ignored)).MustNotHaveHappened();
        }

        // ------------------------------------------------------------------ el SQL, clavado

        [TestMethod]
        public void SqlFecharEntrada_PoneLaFechaDeAhoraSoloALasFilasDeEntradaDelTraspasoEnElDiarioDelDestino()
        {
            string sql = InventariosTrasEnvioSql.SQL_FECHAR_ENTRADA_AHORA;

            StringAssert.Contains(sql, "UPDATE PreExtrProducto SET Fecha = GETDATE()");
            StringAssert.Contains(sql, "Empresa = @p0");
            StringAssert.Contains(sql, "[Almacén] = @p1");
            StringAssert.Contains(sql, "Diario = @p2");
            StringAssert.Contains(sql, "[NºTraspaso] = @p3");
            // Solo PreExtrProducto: la salida del origen (ya en ExtractoProducto, con la fecha del envío) no se toca
            Assert.IsFalse(sql.Contains("ExtractoProducto "), "no toca ExtractoProducto");
            Assert.IsFalse(sql.Contains("Delegación"), "no toca el origen");
        }

        [TestMethod]
        public void SqlInventariosTrasEnvio_MiraLoMismoQueElTrigger()
        {
            // trgExtractoProductoUpd: inserted P inner join Inventarios I on P.número = I.número and P.almacén = I.almacén
            // where I.estado >= 2 and P.fecha < I.fecha (sin empresa)
            string sql = InventariosTrasEnvioSql.SQL_INVENTARIOS_TRAS_ENVIO;

            StringAssert.Contains(sql, "JOIN Inventarios i ON i.[Número] = p.[Número] AND i.[Almacén] = p.[Almacén] AND i.Estado >= 2 AND p.Fecha < i.Fecha");
            StringAssert.Contains(sql, "a.DiarioEntradaRep = p.Diario");
            StringAssert.Contains(sql, "p.Empresa = @p0 AND p.[Almacén] = @p1 AND p.[NºTraspaso] = @p2 AND p.Cantidad > 0");
            StringAssert.Contains(sql, "CAST(i.StockReal - i.StockCalculado AS int) AS Regularizado");
            StringAssert.Contains(sql, "i.Fecha AS FechaInventario");
            StringAssert.Contains(sql, "RTRIM(i.Usuario) AS Usuario");
            StringAssert.Contains(sql, "p.Fecha AS FechaEnvio");
        }

        // ------------------------------------------------------------------ el aviso

        [TestMethod]
        public void Aviso_DiceProductoFechaHoraRegularizacionYQuien()
        {
            string aviso = InventariosTrasEnvioSql.Aviso(Inventario42267());

            Assert.AreEqual("El 42267 se regularizó por inventario el 26/10 a las 14:19 (+1, Paloma), después de enviar la reposición: " +
                "compruebe que no se cuenta dos veces.", aviso);
        }

        [TestMethod]
        public void Aviso_SinRegularizacionNiUsuario_SoloProductoYFecha()
        {
            // 81028: el 43992 en Algete el 19/10 a las 17:03 (+2, Santiago)
            string aviso = InventariosTrasEnvioSql.Aviso(new InventarioTrasEnvio
            {
                Producto = "43992 ", FechaInventario = new DateTime(2026, 10, 19, 17, 3, 10), Regularizado = 0, Usuario = null
            });

            Assert.AreEqual("El 43992 se regularizó por inventario el 19/10 a las 17:03, después de enviar la reposición: compruebe que no se cuenta dos veces.", aviso);
        }

        [TestMethod]
        public void Aviso_RegularizacionNegativa_LlevaElSigno()
        {
            string aviso = InventariosTrasEnvioSql.Aviso(new InventarioTrasEnvio
            {
                Producto = "40275", FechaInventario = new DateTime(2026, 10, 27, 10, 13, 30), Regularizado = -3, Usuario = "NUEVAVISION\\Alfredo"
            });

            StringAssert.Contains(aviso, "(-3, Alfredo)");
        }

        [TestMethod]
        public void Avisos_UnoPorProducto_ElUltimoInventarioSiHayVarios_YNingunoSinInventarios()
        {
            var antes = Inventario42267();
            var despues = Inventario42267();
            despues.FechaInventario = new DateTime(2026, 10, 27, 9, 0, 0);
            despues.Regularizado = -1;

            List<string> avisos = InventariosTrasEnvioSql.Avisos(new[] { antes, despues, new InventarioTrasEnvio { Producto = "43992", FechaInventario = new DateTime(2026, 10, 19, 17, 3, 0), Regularizado = 2 } });

            Assert.AreEqual(2, avisos.Count);
            StringAssert.Contains(avisos[0], "El 42267 se regularizó por inventario el 27/10 a las 09:00 (-1, Paloma)");
            StringAssert.Contains(avisos[1], "El 43992 se regularizó por inventario el 19/10 a las 17:03 (+2)");
            Assert.AreEqual(0, InventariosTrasEnvioSql.Avisos(null).Count);
            Assert.AreEqual(0, InventariosTrasEnvioSql.Avisos(new List<InventarioTrasEnvio>()).Count);
        }

        [TestMethod]
        public async Task Terminar_ConInventarioPosteriorAlEnvio_EntraYAvisa()
        {
            // 81177: con la fecha del envío el trigger la bloqueaba; ahora entra (fechada al recibir) y se avisa de la posible doble cuenta
            A.CallTo(() => transaccion.LeerInventariosTrasEnvio("1", "ALC", TRASPASO)).Returns(new List<InventarioTrasEnvio> { Inventario42267() });

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones().Terminar(Solicitud(false, ("42267", 1), ("45131", 1)));

            A.CallTo(() => transaccion.Contabilizar("1", "RepoAlgAlc", "NUEVAVISION\\Paloma")).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.Avisos.Count(a => a.StartsWith("El 42267 se regularizó por inventario el 26/10 a las 14:19")));
            Assert.AreEqual(0, resultado.Diferencias.Count, "no es una diferencia: ha entrado lo enviado");
        }

        [TestMethod]
        public async Task Terminar_EnParteConInventarioPosterior_AvisaDespuesDelAvisoDeParcial()
        {
            A.CallTo(() => transaccion.LeerInventariosTrasEnvio("1", "ALC", TRASPASO)).Returns(new List<InventarioTrasEnvio> { Inventario42267() });

            ResultadoTerminarRecepcionDTO resultado = await Reposiciones().Terminar(Solicitud(true, ("42267", 1)));

            Assert.IsTrue(resultado.Parcial);
            Assert.AreEqual(2, resultado.Avisos.Count);
            StringAssert.StartsWith(resultado.Avisos[0], "Se ha dado de alta lo leído");
            StringAssert.StartsWith(resultado.Avisos[1], "El 42267 se regularizó por inventario");
        }

        [TestMethod]
        public async Task LeerEsperado_ConInventarioPosteriorAlEnvio_LlevaElAvisoEnLaRecepcion()
        {
            // Para enseñarlo antes de leer (Ariadna y Nesto): el aviso va en RecepcionDTO.Avisos
            A.CallTo(() => servicio.LeerRecepcion("1", "ALC", TRASPASO)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1", Almacen = "ALC", Traspaso = TRASPASO,
                Lineas = new List<LineaReposicionDTO> { new LineaReposicionDTO { Producto = "42267", Cantidad = 1 } }
            });
            A.CallTo(() => servicio.LeerPendientes("1", "ALC")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = TRASPASO, Origen = "ALG", NombreOrigen = "Algete" }
            });
            A.CallTo(() => servicio.AvisosInventariosTrasEnvio("1", "ALC", TRASPASO)).Returns(new List<string> { InventariosTrasEnvioSql.Aviso(Inventario42267()) });

            RecepcionDTO recepcion = await Reposiciones().LeerEsperado("1", "ALC", TRASPASO.ToString());

            Assert.AreEqual(1, recepcion.Avisos.Count);
            StringAssert.StartsWith(recepcion.Avisos[0], "El 42267 se regularizó por inventario el 26/10 a las 14:19 (+1, Paloma)");
        }

        [TestMethod]
        public async Task LeerEsperado_SinInventarios_AvisosVacios()
        {
            A.CallTo(() => servicio.LeerRecepcion("1", "ALC", TRASPASO)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1", Almacen = "ALC", Traspaso = TRASPASO,
                Lineas = new List<LineaReposicionDTO> { new LineaReposicionDTO { Producto = "42267", Cantidad = 1 } }
            });
            A.CallTo(() => servicio.LeerPendientes("1", "ALC")).Returns(new List<ReposicionPendienteDTO>());
            A.CallTo(() => servicio.AvisosInventariosTrasEnvio("1", "ALC", TRASPASO)).Returns(new List<string>());

            RecepcionDTO recepcion = await Reposiciones().LeerEsperado("1", "ALC", TRASPASO.ToString());

            Assert.IsNotNull(recepcion.Avisos);
            Assert.AreEqual(0, recepcion.Avisos.Count);
        }

        [TestMethod]
        public async Task ServicioRecepcionReposiciones_AvisosInventariosTrasEnvio_MontaLosAvisosDelRepositorio()
        {
            var repositorio = A.Fake<IRepositorioRecepcionReposiciones>();
            A.CallTo(() => repositorio.LeerInventariosTrasEnvio("1", "ALC", TRASPASO)).Returns(new List<InventarioTrasEnvio> { Inventario42267() });

            List<string> avisos = await new ServicioRecepcionReposiciones(repositorio).AvisosInventariosTrasEnvio("1", "ALC", TRASPASO);

            Assert.AreEqual(1, avisos.Count);
            StringAssert.StartsWith(avisos[0], "El 42267 se regularizó por inventario");
        }
    }
}
