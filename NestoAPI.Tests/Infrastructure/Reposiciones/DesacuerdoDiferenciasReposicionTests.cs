using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>
    /// NestoAPI#906 (sugerencia 1433, Santiago): Algete envía un esmalte a Reina, Reina dice que no lo ha recibido y Algete ha comprobado
    /// que salió. «En discusión», la regla de imputación (Carlos 25/11/26: decide el sistema, no Dirección), el indicador de errores
    /// demostrados y la verdad a posteriori por inventario.
    /// </summary>
    [TestClass]
    public class DesacuerdoDiferenciasReposicionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 25, 10, 0, 0);

        private static PruebasDesacuerdo Pruebas(int enviado, int escaneadas, params BultoDesacuerdo[] bultos)
            => new PruebasDesacuerdo { Enviado = enviado, EscaneadasOrigen = escaneadas, Bultos = bultos.ToList() };

        private static BultoDesacuerdo Bulto(string codigo, DateTime? entregado = null, DateTime? leido = null)
            => new BultoDesacuerdo { Codigo = codigo, Entregado = entregado, LeidoAlRecibir = leido };

        private static IndicadorErroresAlmacen Indicador(string almacen, int errores, int discrepancias)
            => new IndicadorErroresAlmacen { Almacen = almacen, Errores = errores, Discrepancias = discrepancias };

        private static CasoImputacionDesacuerdo Caso(int unidades, PruebasDesacuerdo pruebas, IndicadorErroresAlmacen o = null, IndicadorErroresAlmacen d = null,
            string turno = null)
            => new CasoImputacionDesacuerdo { Origen = "ALG", Destino = "REI", Unidades = unidades, Pruebas = pruebas, IndicadorOrigen = o, IndicadorDestino = d, UltimoTurno = turno };

        // ---------------------------------------------------------------- regla de imputación

        [TestMethod]
        public void Regla_CasoEsmalte_AlgeteLoLeyoYElBultoSeEntregoEnReina_VaAReina()
        {
            List<ImputacionUnidadDesacuerdo> decision = ReglaImputacionDesacuerdo.Decidir(
                Caso(1, Pruebas(1, 1, Bulto("R81463-2", entregado: new DateTime(2026, 11, 24, 12, 53, 0)))));

            Assert.AreEqual("REI", decision.Single().Almacen);
            Assert.AreEqual(PasosImputacionDesacuerdo.PRUEBA, decision.Single().Paso);
            Assert.AreEqual("Va a Reina: Algete la leyó al enviar y el bulto R81463-2 se entregó en Reina el 24/11.", decision.Single().Motivo);
        }

        [TestMethod]
        public void Regla_SinLecturaDelOrigen_VaAlOrigenAunqueElBultoLlegase()
        {
            List<ImputacionUnidadDesacuerdo> decision = ReglaImputacionDesacuerdo.Decidir(
                Caso(1, Pruebas(2, 1, Bulto("R81463-1", leido: HOY))));

            Assert.AreEqual("ALG", decision.Single().Almacen);
            Assert.AreEqual(PasosImputacionDesacuerdo.PRUEBA, decision.Single().Paso);
            Assert.AreEqual("Va a Algete: Algete no la leyó al enviar (leyó 1 de 2).", decision.Single().Motivo);
        }

        [TestMethod]
        public void Regla_UnaUnidadNoSeParte_LasNoLeidasAlOrigenYLasLeidasAlDestino()
        {
            List<ImputacionUnidadDesacuerdo> decision = ReglaImputacionDesacuerdo.Decidir(
                Caso(3, Pruebas(5, 3, Bulto("R81463-1", entregado: HOY), Bulto("R81463-2", leido: HOY))));

            CollectionAssert.AreEqual(new[] { "ALG", "ALG", "REI" }, decision.Select(d => d.Almacen).ToList());
            StringAssert.Contains(decision[2].Motivo, "sus 2 bultos (R81463-1 a R81463-2) llegaron a Reina");
        }

        [TestMethod]
        public void Regla_LeidaPeroSinConstarQueLlegaseElBulto_VaAlDePeorIndicador()
        {
            List<ImputacionUnidadDesacuerdo> decision = ReglaImputacionDesacuerdo.Decidir(
                Caso(1, Pruebas(1, 1, Bulto("R81463-1", entregado: HOY), Bulto("R81463-2")), Indicador("ALG", 1, 20), Indicador("REI", 3, 10)));

            Assert.AreEqual("REI", decision.Single().Almacen);
            Assert.AreEqual(PasosImputacionDesacuerdo.INDICADOR, decision.Single().Paso);
            StringAssert.Contains(decision.Single().Motivo, "Reina tiene peor indicador de errores demostrados en 12 meses (30 % frente a 5 %)");
        }

        [TestMethod]
        public void Regla_MismoIndicadorOSinDatos_SeAlternaEmpezandoPorElOrigen()
        {
            List<ImputacionUnidadDesacuerdo> primera = ReglaImputacionDesacuerdo.Decidir(Caso(3, Pruebas(3, 3)));

            CollectionAssert.AreEqual(new[] { "ALG", "REI", "ALG" }, primera.Select(d => d.Almacen).ToList());
            Assert.IsTrue(primera.All(d => d.Paso == PasosImputacionDesacuerdo.ALTERNANCIA));
            StringAssert.Contains(primera[0].Motivo, "es la primera entre Algete y Reina");
            StringAssert.Contains(primera[0].Motivo, "sin datos del indicador de errores");
        }

        [TestMethod]
        public void Regla_Alternancia_SigueElTurnoDeLaUltimaDeLaPareja()
        {
            List<ImputacionUnidadDesacuerdo> decision = ReglaImputacionDesacuerdo.Decidir(
                Caso(1, Pruebas(1, 1), Indicador("ALG", 1, 10), Indicador("REI", 1, 10), turno: "ALG"));

            Assert.AreEqual("REI", decision.Single().Almacen);
            StringAssert.Contains(decision.Single().Motivo, "con el mismo indicador de errores; le toca por turno (la anterior fue a Algete)");
        }

        [TestMethod]
        public void Regla_TecleadoNoEsLectura()
        {
            // Lo tecleado (MANUAL) no cuenta: el lector solo suma SCAN. Sin escaneos, todo al origen.
            Assert.AreEqual("ALG", ReglaImputacionDesacuerdo.Decidir(Caso(1, Pruebas(1, 0, Bulto("R1-1", entregado: HOY)))).Single().Almacen);
        }

        // ---------------------------------------------------------------- stock

        [TestMethod]
        public void Stock_ImputadaAlDestinoConAjusteHecho_NoVuelveAlOrigenYLaBajaQuedaEnElDestino()
        {
            List<MovimientoStockDiferencia> m = PlanStockDesacuerdo.Movimientos("1", 81463, "ALG", "REI", "41752", ajusteHecho: true, unidades: 1,
                alOrigen: 0, alDestino: 1, usuario: "u");

            Assert.AreEqual(3, m.Count);
            Assert.AreEqual(("ALG", -1, "Desacuerdo de reposición 81463 (no vuelve)"), (m[0].Almacen, m[0].Cantidad, m[0].Texto));
            Assert.AreEqual(("REI", 1, "Desacuerdo de reposición 81463 (llegó)"), (m[1].Almacen, m[1].Cantidad, m[1].Texto));
            Assert.AreEqual(("REI", -1, "Desacuerdo de reposición 81463 (pérdida)"), (m[2].Almacen, m[2].Cantidad, m[2].Texto));
            Assert.AreEqual(-1, m.Where(x => x.Almacen == "ALG").Sum(x => x.Cantidad), "Lo que volvió con el ajuste sale del origen");
            Assert.AreEqual(0, m.Where(x => x.Almacen == "REI").Sum(x => x.Cantidad));
            Assert.IsTrue(m.All(x => x.Texto.Length <= 50));
        }

        [TestMethod]
        public void Stock_ImputadaAlOrigenConAjusteHecho_BajaEnElOrigen()
        {
            List<MovimientoStockDiferencia> m = PlanStockDesacuerdo.Movimientos("1", 81463, "ALG", "REI", "41752", true, 1, 1, 0, "u");

            Assert.AreEqual(("ALG", -1, "Desacuerdo de reposición 81463 (pérdida)"), (m.Single().Almacen, m.Single().Cantidad, m.Single().Texto));
        }

        [TestMethod]
        public void Stock_SinAjusteAlRecibir_ElSaldoNoCambiaPeroConstaLaBaja()
        {
            List<MovimientoStockDiferencia> m = PlanStockDesacuerdo.Movimientos("1", 81463, "ALG", "REI", "41752", false, 2, 1, 1, "u");

            Assert.AreEqual(0, m.Where(x => x.Almacen == "ALG").Sum(x => x.Cantidad));
            Assert.AreEqual(0, m.Where(x => x.Almacen == "REI").Sum(x => x.Cantidad));
            Assert.AreEqual(1, m.Count(x => x.Almacen == "ALG" && x.Cantidad < 0 && x.Texto.EndsWith("(pérdida)")));
            Assert.AreEqual(1, m.Count(x => x.Almacen == "REI" && x.Cantidad < 0 && x.Texto.EndsWith("(pérdida)")));
        }

        // ---------------------------------------------------------------- indicador

        [TestMethod]
        public void Indicador_SoloCuentanLosErroresDemostrados()
        {
            var filas = new List<DiferenciaParaIndicador>
            {
                new DiferenciaParaIndicador { Id = 1, Origen = "ALG", Destino = "REI", Diferencia = -1, Resolucion = ResolucionesDiferenciaReposicion.ERROR_RECEPCION, UsuarioRecepcion = "NUEVAVISION\\Reina", Preparador = "Andre" },
                new DiferenciaParaIndicador { Id = 2, Origen = "ALG", Destino = "REI", Diferencia = -1, Resolucion = ResolucionesDiferenciaReposicion.CONFIRMADA, Preparador = "Andre" },
                new DiferenciaParaIndicador { Id = 3, Origen = "ALG", Destino = "ALC", Diferencia = -1, Resolucion = ResolucionesDiferenciaReposicion.DESACUERDO },
                new DiferenciaParaIndicador { Id = 4, Origen = "ALG", Destino = "ALC", Diferencia = -1, Resolucion = ResolucionesDiferenciaReposicion.DESACUERDO, ErroresDemostradosDe = new List<string> { "ALC" } },
                new DiferenciaParaIndicador { Id = 5, Origen = "ALC", Destino = "ALG", Diferencia = 1, Resolucion = null }
            };

            List<IndicadorErroresAlmacen> indicadores = ReglaIndicadorErrores.PorAlmacen(filas);

            IndicadorErroresAlmacen alg = indicadores.Single(i => i.Almacen == "ALG");
            Assert.AreEqual((5, 1), (alg.Discrepancias, alg.Errores), "El desacuerdo 3 decidido por alternancia/indicador no cuenta");
            Assert.AreEqual((2, 1), (indicadores.Single(i => i.Almacen == "REI").Discrepancias, indicadores.Single(i => i.Almacen == "REI").Errores));
            Assert.AreEqual(0.3333M, indicadores.Single(i => i.Almacen == "ALC").Valor);
            List<IndicadorErroresUsuario> usuarios = ReglaIndicadorErrores.PorUsuario(filas);
            Assert.AreEqual((2, 1), (usuarios.Single(u => u.Usuario == "Andre").Discrepancias, usuarios.Single(u => u.Usuario == "Andre").Errores));
            Assert.AreEqual(1, usuarios.Single(u => u.Usuario == "Reina").Errores, "Sin dominio");
        }

        [TestMethod]
        public void Indicador_SinDiscrepancias_SinDatos()
        {
            Assert.IsNull(Indicador("ALG", 0, 0).Valor);
        }

        // ---------------------------------------------------------------- verdad a posteriori

        private static CasoSinVerdad SinVerdad(int id, int unidades = 1, string producto = "41752")
            => new CasoSinVerdad { DiscusionId = id, Producto = producto, Origen = "ALG", Destino = "REI", Unidades = unidades, Fecha = new DateTime(2026, 11, 24) };

        private static SobranteInventario Sobrante(int numOrden, string almacen, int sobrante, DateTime? fecha = null, string producto = "41752")
            => new SobranteInventario { NumOrden = numOrden, Almacen = almacen, Producto = producto, Sobrante = sobrante, Fecha = fecha ?? new DateTime(2027, 1, 10) };

        [TestMethod]
        public void Verdad_SobranteEnElOrigen_NoSalio()
        {
            VerdadAsignada v = ReglaVerdadInventario.Asignar(new[] { SinVerdad(1) }, new[] { Sobrante(900, "ALG", 1) }, null).Single();

            Assert.AreEqual(("ALG", 900), (v.Almacen, v.Inventario));
        }

        [TestMethod]
        public void Verdad_UnSobranteSoloGastaUnaVez()
        {
            List<VerdadAsignada> v = ReglaVerdadInventario.Asignar(new[] { SinVerdad(1), SinVerdad(2) }, new[] { Sobrante(900, "REI", 2) }, null);

            Assert.AreEqual(1, v.Count);
            Assert.AreEqual(1, v.Single().DiscusionId, "El más antiguo primero");
        }

        [TestMethod]
        public void Verdad_SiSobraEnLosDos_NoSeDecide()
        {
            Assert.AreEqual(0, ReglaVerdadInventario.Asignar(new[] { SinVerdad(1) }, new[] { Sobrante(900, "ALG", 1), Sobrante(901, "REI", 1) }, null).Count);
        }

        [TestMethod]
        public void Verdad_InventarioAnteriorPocoSobranteOYaGastado_NoValen()
        {
            Assert.AreEqual(0, ReglaVerdadInventario.Asignar(new[] { SinVerdad(1, unidades: 2) }, new[]
            {
                Sobrante(900, "ALG", 5, fecha: new DateTime(2026, 11, 1)),
                Sobrante(901, "ALG", 1),
                Sobrante(902, "ALG", 3, producto: "OTRO")
            }, null).Count);
            Assert.AreEqual(0, ReglaVerdadInventario.Asignar(new[] { SinVerdad(1) }, new[] { Sobrante(900, "ALG", 1) }, new[] { 900 }).Count);
        }

        [TestMethod]
        public void Verdad_CasosSinVerdad_SoloLosDecididosYSinNingunaVerdad()
        {
            var filas = new List<ImputacionDesacuerdoFila>
            {
                new ImputacionDesacuerdoFila { DiscusionId = 1, Unidad = 1, AlmacenImputado = "REI", Producto = "41752", Origen = "ALG", Destino = "REI", FechaRecepcion = HOY },
                new ImputacionDesacuerdoFila { DiscusionId = 1, Unidad = 2, AlmacenImputado = "ALG", Producto = "41752", Origen = "ALG", Destino = "REI", FechaRecepcion = HOY },
                new ImputacionDesacuerdoFila { DiscusionId = 2, Unidad = 1, AlmacenImputado = null },
                new ImputacionDesacuerdoFila { DiscusionId = 3, Unidad = 1, AlmacenImputado = "ALG", ErrorDemostradoDe = "ALG" }
            };

            CasoSinVerdad caso = ServicioDiscusionesDiferencias.CasosSinVerdad(filas).Single();

            Assert.AreEqual((1, 2), (caso.DiscusionId, caso.Unidades));
        }

        [TestMethod]
        public void Verdad_ResumenPorPaso()
        {
            var filas = new List<ImputacionDesacuerdoFila>
            {
                new ImputacionDesacuerdoFila { DiscusionId = 1, Paso = "Prueba", AlmacenImputado = "REI", ErrorDemostradoDe = "REI" },
                new ImputacionDesacuerdoFila { DiscusionId = 2, Paso = "Alternancia", AlmacenImputado = "ALG", ErrorDemostradoDe = "REI" },
                new ImputacionDesacuerdoFila { DiscusionId = 2, Paso = "Alternancia", AlmacenImputado = "REI", ErrorDemostradoDe = "REI" },
                new ImputacionDesacuerdoFila { DiscusionId = 3, Paso = "Indicador", AlmacenImputado = "ALG" },
                new ImputacionDesacuerdoFila { DiscusionId = 4 }
            };

            ResumenVerdadDesacuerdoDTO resumen = ResumenVerdadDesacuerdo.Calcular(filas, HOY);

            Assert.AreEqual((3, 2, 4, 3), (resumen.Casos, resumen.CasosConVerdad, resumen.Unidades, resumen.UnidadesConVerdad));
            Assert.AreEqual(100M, resumen.PorPaso.Single(p => p.Paso == "Prueba").PorcentajeAcierto);
            Assert.AreEqual(50M, resumen.PorPaso.Single(p => p.Paso == "Alternancia").PorcentajeAcierto);
            Assert.IsNull(resumen.PorPaso.Single(p => p.Paso == "Indicador").PorcentajeAcierto);
            Assert.AreEqual("Regla", resumen.QuienManda);
        }

        [TestMethod]
        public void Modelo_HoySiempreMandaLaRegla()
        {
            Assert.AreEqual("Regla", ModeloImputacionDesacuerdo.QuienManda("Modelo", hayModelo: false));
            Assert.AreEqual("Modelo", ModeloImputacionDesacuerdo.QuienManda("Modelo", hayModelo: true));
            Assert.AreEqual("Regla", ModeloImputacionDesacuerdo.QuienManda(null, hayModelo: true));
        }

        // ---------------------------------------------------------------- el servicio

        private ITransaccionDiferenciasReposicion transaccion;
        private IRepositorioDiferenciasReposicion repoDiferencias;
        private IRepositorioDiscusionesDiferencias repo;
        private IAvisadorDiscusionesDiferencias avisador;
        private DiferenciaReposicionDTO fila;
        private DiscusionDiferenciaDTO discusion;

        [TestInitialize]
        public void Inicializar()
        {
            transaccion = A.Fake<ITransaccionDiferenciasReposicion>();
            repoDiferencias = A.Fake<IRepositorioDiferenciasReposicion>();
            A.CallTo(() => repoDiferencias.EnTransaccion(A<Func<ITransaccionDiferenciasReposicion, Task<DiferenciaReposicionDTO>>>._))
                .ReturnsLazily((Func<ITransaccionDiferenciasReposicion, Task<DiferenciaReposicionDTO>> trabajo) => trabajo(transaccion));
            repo = A.Fake<IRepositorioDiscusionesDiferencias>();
            A.CallTo(() => repo.TablasCreadas()).Returns(true);
            avisador = A.Fake<IAvisadorDiscusionesDiferencias>();
            // El esmalte: Algete envió 1, Reina leyó 0; al recibir volvió a contar en Algete (ajuste hecho)
            fila = new DiferenciaReposicionDTO
            {
                Id = 7, Empresa = "1", Traspaso = 81463, Origen = "ALG", Destino = "REI", Producto = "41752", Descripcion = "ESMALTE ROJO",
                Enviado = 1, Leido = 0, Diferencia = -1, Estado = EstadosDiferenciaReposicion.PENDIENTE, Ajuste = AjustesDiferenciaReposicion.HECHO,
                OrigenConControlUbicaciones = true, Preparador = "NUEVAVISION\\Andre", UsuarioRecepcion = "NUEVAVISION\\Reina", FechaRecepcion = HOY.AddDays(-1)
            };
            A.CallTo(() => repoDiferencias.Leer("1", 7)).Returns(fila);
            A.CallTo(() => transaccion.Leer("1", 7)).Returns(fila);
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).Returns(new ResultadoMovimientoStock { NumeroOrdenExtracto = 123 });
            discusion = new DiscusionDiferenciaDTO
            {
                Id = 3, DiferenciaId = 7, Empresa = "1", Traspaso = 81463, Origen = "ALG", Destino = "REI", Producto = "41752", Unidades = 1,
                Estado = EstadosDiscusionDiferencia.ABIERTA, Plazo = HOY.AddHours(30), ComentarioOrigen = "Salió del 005/005/004"
            };
            A.CallTo(() => repo.LeerPruebas("1", 81463, "41752", 1))
                .Returns(Pruebas(1, 1, Bulto("R81463-2", entregado: new DateTime(2026, 11, 24, 12, 53, 0))));
            A.CallTo(() => repo.LeerParaIndicador(A<string>._, A<DateTime>._)).Returns(new List<DiferenciaParaIndicador>());
        }

        private ServicioDiscusionesDiferencias Servicio(bool interruptor = true)
        {
            var almacenes = new Dictionary<string, string> { ["Reina"] = "REI", ["Paloma"] = "ALC" };
            var diferencias = new ServicioDiferenciasReposicion(repoDiferencias, () => interruptor, (e, u) => almacenes.TryGetValue(u, out string a) ? a : null,
                A.Fake<IAvisadorDevolucionStockOrigen>());
            return new ServicioDiscusionesDiferencias(repo, repoDiferencias, diferencias, avisador, () => HOY, () => 48);
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        [TestMethod]
        public async Task Abrir_ElOrigenAbreYSeGuardanLosRasgosYSeAvisaAlDestino()
        {
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Returns((DiscusionDiferenciaDTO)null);
            A.CallTo(() => repo.LeerPvp("1", "41752")).Returns(8.95M);
            A.CallTo(() => repo.LeerRepartidor("1", 81463)).Returns("Miguel");
            List<ImputacionDesacuerdoFila> guardadas = null;
            A.CallTo(() => repo.Abrir(A<DiscusionDiferenciaDTO>._, A<IEnumerable<ImputacionDesacuerdoFila>>._))
                .Invokes((DiscusionDiferenciaDTO d, IEnumerable<ImputacionDesacuerdoFila> u) => guardadas = u.ToList()).Returns(3);

            ResultadoDiscusionDiferenciaDTO r = await Servicio().Abrir("1", 7, new AbrirDiscusionDiferenciaDTO { Comentario = "Salió del 005/005/004" },
                Usuario("Santi", "Almacén"));

            Assert.AreEqual(HOY.AddHours(48), r.Discusion.Plazo);
            ImputacionDesacuerdoFila unidad = guardadas.Single();
            Assert.AreEqual((8.95M, "Miguel", true, (short)1, (short)1), (unidad.Pvp, unidad.Repartidor, unidad.LecturaOrigen, unidad.Bultos, unidad.BultosEnDestino));
            Assert.IsNull(unidad.AlmacenImputado, "Al abrir no se decide nada");
            A.CallTo(() => avisador.Avisar("1", A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "REI" }), false, A<string>._, A<string>._, 7))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Abrir_SinComentarioOSinScriptOEnUnaSobra_NoSeAbre()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Abrir("1", 7, new AbrirDiscusionDiferenciaDTO(), Usuario("Santi", "Almacén")));
            fila.Diferencia = 1;
            fila.Leido = 2;
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Abrir("1", 7, new AbrirDiscusionDiferenciaDTO { Comentario = "x" }, Usuario("Santi", "Almacén")));
            A.CallTo(() => repo.TablasCreadas()).Returns(false);
            NestoBusinessException sinScript = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().Abrir("1", 7, new AbrirDiscusionDiferenciaDTO { Comentario = "x" }, Usuario("Santi", "Almacén")));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, sinScript.StatusCode);
            A.CallTo(() => repo.Abrir(A<DiscusionDiferenciaDTO>._, A<IEnumerable<ImputacionDesacuerdoFila>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Abrir_LaTiendaDestinoNoPuedeAbrirla()
        {
            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                Servicio().Abrir("1", 7, new AbrirDiscusionDiferenciaDTO { Comentario = "x" }, Usuario("Reina")));
        }

        [TestMethod]
        public async Task Responder_NoEsta_DecideLaReglaYLaBajaVaAReina()
        {
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Returns(discusion);

            ResultadoDiscusionDiferenciaDTO r = await Servicio().Responder("1", 7,
                new ResponderDiscusionDiferenciaDTO { Respuesta = "NoEsta", Comentario = "No está en la tienda" }, Usuario("Reina"));

            Assert.AreEqual("REI", r.Imputaciones.Single().Almacen);
            Assert.AreEqual(EstadosDiscusionDiferencia.DECIDIDA, r.Discusion.Estado);
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Almacen == "ALG" && m.Cantidad == -1))).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>.That.Matches(m => m.Almacen == "REI" && m.Cantidad == -1 && m.Texto.EndsWith("(pérdida)"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.GuardarResolucion(7, ResolucionesDiferenciaReposicion.DESACUERDO, "Reina", A<string>._, 123, 123))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.GuardarDecision(3, A<string>._, A<IEnumerable<ImputacionUnidadDesacuerdo>>._, A<decimal?>._, A<decimal?>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar("1", A<IEnumerable<string>>.That.IsSameSequenceAs(new[] { "ALG", "REI" }), false, A<string>._, A<string>._, 7))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Responder_ConElAjusteApagado_SeDecidePeroNoSeTocaElStock()
        {
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Returns(discusion);

            ResultadoDiscusionDiferenciaDTO r = await Servicio(interruptor: false).Responder("1", 7, new ResponderDiscusionDiferenciaDTO { Respuesta = "NoEsta" }, Usuario("Reina"));

            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
            StringAssert.Contains(r.Mensaje, "ajuste automático está apagado");
        }

        [TestMethod]
        public async Task Responder_LoHeEncontrado_EsErrorAlRecibirReconocidoPorElDestino()
        {
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Returns(discusion);

            ResultadoDiscusionDiferenciaDTO r = await Servicio().Responder("1", 7, new ResponderDiscusionDiferenciaDTO { Respuesta = "LoHeEncontrado" }, Usuario("Reina"));

            A.CallTo(() => transaccion.GuardarResolucion(7, ResolucionesDiferenciaReposicion.ERROR_RECEPCION, "Reina", A<string>._, A<int?>._, A<int?>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.Acordar(3, A<string>._, "REI", ComoSeDemostroError.RECONOCIDO)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.GuardarDecision(A<int>._, A<string>._, A<IEnumerable<ImputacionUnidadDesacuerdo>>._, A<decimal?>._, A<decimal?>._)).MustNotHaveHappened();
            Assert.AreEqual(EstadosDiscusionDiferencia.ACORDADA, r.Discusion.Estado);
        }

        [TestMethod]
        public async Task Responder_OtraTiendaNoPuede()
        {
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Returns(discusion);

            await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
                Servicio().Responder("1", 7, new ResponderDiscusionDiferenciaDTO { Respuesta = "NoEsta" }, Usuario("Paloma")));
            A.CallTo(() => repo.GuardarRespuesta(A<int>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Revisar_RecordatorioUnaVezYVencidaDecide()
        {
            discusion.Plazo = HOY.AddHours(10);
            var vencida = new DiscusionDiferenciaDTO { Id = 4, DiferenciaId = 7, Traspaso = 81463, Origen = "ALG", Destino = "REI", Producto = "41752", Unidades = 1,
                Estado = EstadosDiscusionDiferencia.ABIERTA, Plazo = HOY.AddMinutes(-1) };
            A.CallTo(() => repo.Abiertas("1")).Returns(new List<DiscusionDiferenciaDTO> { discusion, vencida });

            int decididas = await Servicio().Revisar("1");

            Assert.AreEqual(1, decididas);
            A.CallTo(() => repo.MarcarRecordatorio(3)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repo.GuardarDecision(4, A<string>._, A<IEnumerable<ImputacionUnidadDesacuerdo>>._, A<decimal?>._, A<decimal?>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.GuardarResolucion(7, ResolucionesDiferenciaReposicion.DESACUERDO, ServicioDiscusionesDiferencias.USUARIO_SISTEMA, A<string>._, A<int?>._, A<int?>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AlResolver_ConfirmadaConLaDiscusionAbierta_ErrorReconocidoDelOrigen()
        {
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Returns(discusion);

            await Servicio().AlResolver("1", 7, ResolucionesDiferenciaReposicion.CONFIRMADA);

            A.CallTo(() => repo.Acordar(3, A<string>._, "ALG", ComoSeDemostroError.RECONOCIDO)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AlResolver_SinDiscusionOSinScript_NoHaceNadaNiLanza()
        {
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Returns((DiscusionDiferenciaDTO)null);
            await Servicio().AlResolver("1", 7, ResolucionesDiferenciaReposicion.CONFIRMADA);
            A.CallTo(() => repo.TablasCreadas()).Returns(false);
            await Servicio().AlResolver("1", 7, ResolucionesDiferenciaReposicion.CONFIRMADA);
            A.CallTo(() => repo.LeerPorDiferencia("1", 7)).Throws(new InvalidOperationException("sin BD"));
            A.CallTo(() => repo.TablasCreadas()).Returns(true);
            await Servicio().AlResolver("1", 7, ResolucionesDiferenciaReposicion.CONFIRMADA);

            A.CallTo(() => repo.Acordar(A<int>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Completar_MarcaLasFaltasQueSePuedenDiscutirYNuncaRompe()
        {
            var sobra = new DiferenciaReposicionDTO { Id = 8, Origen = "ALC", Destino = "ALG", Diferencia = 1, Estado = EstadosDiferenciaReposicion.PENDIENTE };
            A.CallTo(() => repo.LeerPorDiferencias("1", A<IEnumerable<int>>._)).Returns(new List<DiscusionDiferenciaDTO>());

            await Servicio().Completar("1", new[] { fila, sobra });

            Assert.IsTrue(fila.PuedeAbrirDiscusion);
            Assert.IsFalse(sobra.PuedeAbrirDiscusion);
            A.CallTo(() => repo.LeerPorDiferencias("1", A<IEnumerable<int>>._)).Throws(new InvalidOperationException("x"));
            await Servicio().Completar("1", new[] { fila });
        }

        [TestMethod]
        public async Task BuscarVerdad_ContradiceALaRegla_SeApuntaYSeAvisaADireccionSinDeshacer()
        {
            A.CallTo(() => repo.Imputaciones(A<DateTime>._)).Returns(new List<ImputacionDesacuerdoFila>
            {
                new ImputacionDesacuerdoFila { DiscusionId = 3, Unidad = 1, Traspaso = 81463, Producto = "41752", Origen = "ALG", Destino = "REI", AlmacenImputado = "REI", FechaRecepcion = HOY.AddDays(-30) }
            });
            A.CallTo(() => repo.Sobrantes("1", A<IEnumerable<string>>._, A<DateTime>._)).Returns(new List<SobranteInventario>
            {
                new SobranteInventario { NumOrden = 950001, Almacen = "ALG", Producto = "41752", Fecha = HOY.AddDays(-2), Sobrante = 1 }
            });

            int verdades = await Servicio().BuscarVerdad("1");

            Assert.AreEqual(1, verdades);
            A.CallTo(() => repo.GuardarVerdad(3, "ALG", ComoSeDemostroError.INVENTARIO, HOY.AddDays(-2), 950001)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar("1", A<IEnumerable<string>>._, true, A<string>._, A<string>._, 0)).MustHaveHappenedOnceExactly();
            A.CallTo(() => transaccion.Mover(A<MovimientoStockDiferencia>._)).MustNotHaveHappened();
        }
    }
}
