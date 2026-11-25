using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Proyectos;
using NestoAPI.Models.Proyectos;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Proyectos
{
    /// <summary>NestoAPI#901: LAS REGLAS de los proyectos (código, ciclo de vida, signos, reparto de un apunte y cuenta de explotación).</summary>
    [TestClass]
    public class ReglasProyectosTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 25, 10, 0, 0);

        // ------------------------------------------------------------ Código y datos

        [DataTestMethod]
        [DataRow("IMPE0210-2026-1")]
        [DataRow("impe0210-2026-1")]
        [DataRow("FERIA_2026")]
        [DataRow("BF2026")]
        [DataRow("A/B.C")]
        [DataRow("12345678901234567890")]
        public void Codigo_Valido(string codigo)
        {
            Assert.IsNull(ReglasProyectos.MotivoCodigoNoValido(codigo));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("   ")]
        [DataRow("123456789012345678901")]
        [DataRow("-IMPE")]
        [DataRow("IMPE 0210")]
        [DataRow("CAMPAÑA")]
        [DataRow("IMPE#1")]
        public void Codigo_NoValido(string codigo)
        {
            Assert.IsNotNull(ReglasProyectos.MotivoCodigoNoValido(codigo));
        }

        [TestMethod]
        public void Codigo_SeGuardaEnMayusculasYSinEspaciosAlrededor()
        {
            Assert.AreEqual("IMPE0210-2026-1", ReglasProyectos.NormalizarCodigo("  impe0210-2026-1 "));
        }

        [DataTestMethod]
        [DataRow("curso sepe", "CursoSEPE")]
        [DataRow("Campaña", "Campana")]
        [DataRow("FERIA", "Feria")]
        [DataRow("otro", "Otro")]
        [DataRow("curso", null)]
        public void Tipo_SeNormaliza(string tipo, string esperado)
        {
            Assert.AreEqual(esperado, ReglasProyectos.NormalizarTipo(tipo));
        }

        [TestMethod]
        public void Validar_DatosBuenos_LosDejaLimpios()
        {
            List<string> faltas = ReglasProyectos.Validar(new DatosProyecto
            {
                Codigo = " impe0210-2026-1 ",
                Nombre = " IMPE0210 primera convocatoria ",
                Tipo = "curso sepe",
                FechaDesde = new DateTime(2026, 2, 1, 9, 30, 0),
                FechaHasta = new DateTime(2026, 6, 30),
                Producto = " impe0210 ",
                PresupuestoGastos = 1000.456M
            }, out FilaProyecto limpio);

            Assert.AreEqual(0, faltas.Count, string.Join(" ", faltas));
            Assert.AreEqual("1", limpio.Empresa);
            Assert.AreEqual("IMPE0210-2026-1", limpio.Codigo);
            Assert.AreEqual("IMPE0210 primera convocatoria", limpio.Nombre);
            Assert.AreEqual("CursoSEPE", limpio.Tipo);
            Assert.AreEqual(new DateTime(2026, 2, 1), limpio.FechaDesde);
            Assert.AreEqual("IMPE0210", limpio.Producto);
            Assert.AreEqual(1000.46M, limpio.PresupuestoGastos);
            Assert.AreEqual(ReglasProyectos.ESTADO_ABIERTO, limpio.Estado);
        }

        [TestMethod]
        public void Validar_DiceTodoLoQueFalta()
        {
            List<string> faltas = ReglasProyectos.Validar(new DatosProyecto
            {
                Codigo = "",
                Tipo = "Curso",
                FechaHasta = new DateTime(2026, 1, 1),
                PresupuestoIngresos = -1
            }, out _);

            Assert.IsTrue(faltas.Any(f => f.Contains("código")));
            Assert.IsTrue(faltas.Any(f => f.Contains("nombre")));
            Assert.IsTrue(faltas.Any(f => f.Contains("no es un tipo")));
            Assert.IsTrue(faltas.Any(f => f.Contains("fecha de inicio")));
            Assert.IsTrue(faltas.Any(f => f.Contains("negativo")));
        }

        [TestMethod]
        public void Validar_FinAntesQueInicio_NoVale()
        {
            List<string> faltas = ReglasProyectos.Validar(new DatosProyecto
            {
                Codigo = "X", Nombre = "X", Tipo = "Otro", FechaDesde = new DateTime(2026, 5, 1), FechaHasta = new DateTime(2026, 4, 30)
            }, out _);
            Assert.AreEqual(1, faltas.Count);
            StringAssert.Contains(faltas[0], "anterior a la de inicio");
        }

        // ------------------------------------------------------------ Ciclo de vida

        private static FilaProyecto Proyecto(int id = 1, string estado = ReglasProyectos.ESTADO_ABIERTO, string empresa = "1") => new FilaProyecto
        {
            Id = id, Empresa = empresa, Codigo = "IMPE0210-2026-" + id, Nombre = "Convocatoria " + id, Tipo = "CursoSEPE",
            FechaDesde = new DateTime(2026, 1, 1), FechaHasta = new DateTime(2026, 12, 31), Estado = estado
        };

        [TestMethod]
        public void Modificar_Cerrado_409()
        {
            DecisionCambioProyecto d = ReglasProyectos.DecidirModificacion(Proyecto(estado: ReglasProyectos.ESTADO_CERRADO), Proyecto());
            Assert.AreEqual(TipoErrorProyecto.Conflicto, d.Error.Tipo);
            Assert.AreEqual("PROYECTO_CERRADO", d.Error.Codigo);
        }

        [TestMethod]
        public void Modificar_NoCambiaNiEmpresaNiEstado()
        {
            FilaProyecto pedido = Proyecto();
            pedido.Nombre = "Nuevo nombre";
            pedido.Empresa = "3";
            pedido.Estado = ReglasProyectos.ESTADO_CERRADO;
            DecisionCambioProyecto d = ReglasProyectos.DecidirModificacion(Proyecto(), pedido);
            Assert.IsNull(d.Error);
            Assert.IsFalse(d.SinCambios);
            Assert.AreEqual("Nuevo nombre", d.Nuevo.Nombre);
            Assert.AreEqual("1", d.Nuevo.Empresa);
            Assert.AreEqual(ReglasProyectos.ESTADO_ABIERTO, d.Nuevo.Estado);
        }

        [TestMethod]
        public void Modificar_Igual_SinCambios()
        {
            Assert.IsTrue(ReglasProyectos.DecidirModificacion(Proyecto(), Proyecto()).SinCambios);
        }

        [TestMethod]
        public void Cerrar_YReabrir_SonIdempotentes()
        {
            Assert.IsFalse(ReglasProyectos.DecidirCierre(Proyecto()).SinCambios);
            Assert.AreEqual(ReglasProyectos.ESTADO_CERRADO, ReglasProyectos.DecidirCierre(Proyecto()).Nuevo.Estado);
            Assert.IsTrue(ReglasProyectos.DecidirCierre(Proyecto(estado: ReglasProyectos.ESTADO_CERRADO)).SinCambios);
            Assert.IsTrue(ReglasProyectos.DecidirReapertura(Proyecto()).SinCambios);
            Assert.AreEqual(ReglasProyectos.ESTADO_ABIERTO, ReglasProyectos.DecidirReapertura(Proyecto(estado: ReglasProyectos.ESTADO_CERRADO)).Nuevo.Estado);
            Assert.AreEqual(TipoErrorProyecto.NoExiste, ReglasProyectos.DecidirCierre(null).Error.Tipo);
        }

        [TestMethod]
        public void Borrar_SoloAbiertoYSinImputaciones()
        {
            Assert.IsTrue(ReglasProyectos.DecidirBorrado(Proyecto(), 0).Borrar);
            Assert.AreEqual("PROYECTO_CON_IMPUTACIONES", ReglasProyectos.DecidirBorrado(Proyecto(), 2).Error.Codigo);
            Assert.AreEqual("PROYECTO_CERRADO", ReglasProyectos.DecidirBorrado(Proyecto(estado: ReglasProyectos.ESTADO_CERRADO), 0).Error.Codigo);
        }

        // ------------------------------------------------------------ Signos

        [DataTestMethod]
        [DataRow("6230000000", NaturalezaApunte.Gasto)]
        [DataRow("6000001", NaturalezaApunte.Gasto)]
        [DataRow("7050000", NaturalezaApunte.Ingreso)]
        [DataRow("7400000", NaturalezaApunte.Ingreso)]
        [DataRow("4300000", NaturalezaApunte.NoImputable)]
        [DataRow(null, NaturalezaApunte.NoImputable)]
        public void Naturaleza_PorLaCuenta(string cuenta, NaturalezaApunte esperada)
        {
            Assert.AreEqual(esperada, ReglasProyectos.Naturaleza(cuenta));
        }

        [TestMethod]
        public void Signo_GastoAlDebeSuma_GastoAlHaberResta()
        {
            Assert.AreEqual(1, ReglasProyectos.Signo("600", 100, 0));
            Assert.AreEqual(-1, ReglasProyectos.Signo("600", 0, 100));
            Assert.IsTrue(ReglasProyectos.EsAbono("600", 0, 100));
        }

        [TestMethod]
        public void Signo_IngresoAlHaberSuma_IngresoAlDebeResta()
        {
            Assert.AreEqual(1, ReglasProyectos.Signo("705", 0, 100));
            Assert.AreEqual(-1, ReglasProyectos.Signo("705", 100, 0));
            Assert.IsTrue(ReglasProyectos.EsAbono("705", 100, 0));
        }

        [TestMethod]
        public void ImporteApunte_EsElValorAbsolutoDelNeto()
        {
            Assert.AreEqual(100M, ReglasProyectos.ImporteApunte(0, 100));
            Assert.AreEqual(70M, ReglasProyectos.ImporteApunte(100, 30));
        }

        [TestMethod]
        public void Porcentaje_ConDosDecimales()
        {
            Assert.AreEqual(33.33M, ReglasProyectos.Porcentaje(40, 120));
            Assert.AreEqual(0M, ReglasProyectos.Porcentaje(40, 0));
        }

        [TestMethod]
        public void ResultadoYMargen()
        {
            Assert.AreEqual(300M, ReglasProyectos.Resultado(1000, 700));
            Assert.AreEqual(30M, ReglasProyectos.Margen(1000, 700));
            Assert.IsNull(ReglasProyectos.Margen(0, 700));
        }

        // ------------------------------------------------------------ Reparto de un apunte

        private static FilaApunteContable Apunte(string cuenta = "6230000", decimal debe = 120, decimal haber = 0, string empresa = "1") => new FilaApunteContable
        {
            Empresa = empresa, NumeroOrden = 555, Cuenta = cuenta, NombreCuenta = "Libros", Fecha = new DateTime(2026, 3, 1),
            Concepto = "Libros IMPE0210", Documento = "F-12", Asiento = 77, Debe = debe, Haber = haber, Tercero = "1234", TipoTercero = "Proveedor"
        };

        private static ContextoImputacionApunte Contexto(FilaApunteContable apunte, params FilaProyecto[] proyectos)
        {
            var c = new ContextoImputacionApunte { Apunte = apunte };
            foreach (FilaProyecto p in proyectos)
            {
                c.Proyectos[p.Id] = p;
            }
            return c;
        }

        private static List<ImputacionPedida> Pedidas(params (int proyecto, decimal importe)[] lista) =>
            lista.Select(l => new ImputacionPedida { ProyectoId = l.proyecto, Importe = l.importe }).ToList();

        private static FilaImputacion Actual(int id, FilaProyecto p, FilaApunteContable apunte, decimal importe, string comentario = null)
        {
            FilaImputacion f = ReglasProyectos.Foto(apunte);
            f.Id = id;
            f.ProyectoId = p.Id;
            f.Importe = importe;
            f.Comentario = comentario;
            f.Usuario = "Ana";
            f.Fecha = new DateTime(2026, 3, 2);
            f.CodigoProyecto = p.Codigo;
            return f;
        }

        [TestMethod]
        public void Reparto_DosConvocatoriasAMitad_SeInsertanConLaFoto()
        {
            DecisionImputaciones d = ReglasProyectos.DecidirReparto(Contexto(Apunte(), Proyecto(1), Proyecto(2)), 555, 120M,
                Pedidas((1, 60), (2, 60)), "Carlos", AHORA);

            Assert.IsNull(d.Error);
            Assert.AreEqual(2, d.Insertar.Count);
            Assert.IsTrue(d.Insertar.All(i => i.Cuenta == "6230000" && i.Debe == 120 && i.Documento == "F-12" && i.Usuario == "Carlos" && i.Fecha == AHORA));
            Assert.AreEqual(0, d.Avisos.Count);
        }

        [TestMethod]
        public void Reparto_ApunteQueYaNoExiste_409()
        {
            DecisionImputaciones d = ReglasProyectos.DecidirReparto(Contexto(null, Proyecto(1)), 555, null, Pedidas((1, 10)), "Carlos", AHORA);
            Assert.AreEqual(TipoErrorProyecto.Conflicto, d.Error.Tipo);
            Assert.AreEqual("APUNTE_NO_EXISTE", d.Error.Codigo);
        }

        [TestMethod]
        public void Reparto_ApunteQueYaNoExiste_SeQuitanLasHuerfanasSalvoLasDeUnCerrado()
        {
            FilaApunteContable viejo = Apunte();
            FilaProyecto abierto = Proyecto(1);
            ContextoImputacionApunte contexto = Contexto(null, abierto);
            contexto.Actuales.Add(Actual(10, abierto, viejo, 60));
            DecisionImputaciones d = ReglasProyectos.DecidirReparto(contexto, 555, null, Pedidas(), "C", AHORA);
            Assert.IsNull(d.Error);
            CollectionAssert.AreEqual(new List<int> { 10 }, d.IdsABorrar);

            FilaProyecto cerrado = Proyecto(2, ReglasProyectos.ESTADO_CERRADO);
            contexto.Proyectos[2] = cerrado;
            contexto.Actuales.Add(Actual(11, cerrado, viejo, 30));
            Assert.AreEqual("PROYECTO_CERRADO", ReglasProyectos.DecidirReparto(contexto, 555, null, Pedidas(), "C", AHORA).Error.Codigo);
        }

        [TestMethod]
        public void Reparto_ApunteConOtroImporte_409()
        {
            DecisionImputaciones d = ReglasProyectos.DecidirReparto(Contexto(Apunte(debe: 150), Proyecto(1)), 555, 120M, Pedidas((1, 10)), "Carlos", AHORA);
            Assert.AreEqual("APUNTE_CAMBIADO", d.Error.Codigo);
        }

        [TestMethod]
        public void Reparto_PasaDelApunte_400_ConUnCentimoDeTolerancia()
        {
            Assert.IsNull(ReglasProyectos.DecidirReparto(Contexto(Apunte(debe: 100), Proyecto(1), Proyecto(2), Proyecto(3)), 555, null,
                Pedidas((1, 33.34M), (2, 33.34M), (3, 33.33M)), "Carlos", AHORA).Error);

            DecisionImputaciones d = ReglasProyectos.DecidirReparto(Contexto(Apunte(debe: 100), Proyecto(1), Proyecto(2)), 555, null,
                Pedidas((1, 60), (2, 40.02M)), "Carlos", AHORA);
            Assert.AreEqual(TipoErrorProyecto.Invalido, d.Error.Tipo);
            Assert.AreEqual("IMPUTADO_PASA_DEL_APUNTE", d.Error.Codigo);
        }

        [TestMethod]
        public void Reparto_ImporteCeroONegativo_400()
        {
            Assert.AreEqual(TipoErrorProyecto.Invalido, ReglasProyectos.DecidirReparto(Contexto(Apunte(), Proyecto(1)), 555, null, Pedidas((1, 0)), "C", AHORA).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.Invalido, ReglasProyectos.DecidirReparto(Contexto(Apunte(), Proyecto(1)), 555, null, Pedidas((1, -10)), "C", AHORA).Error.Tipo);
        }

        [TestMethod]
        public void Reparto_CuentaQueNoEsDe6Ni7_400()
        {
            DecisionImputaciones d = ReglasProyectos.DecidirReparto(Contexto(Apunte("4000000"), Proyecto(1)), 555, null, Pedidas((1, 10)), "C", AHORA);
            Assert.AreEqual(TipoErrorProyecto.Invalido, d.Error.Tipo);
            StringAssert.Contains(d.Error.Mensaje, "no es de gastos");
        }

        [TestMethod]
        public void Reparto_ProyectoQueNoExisteODeOtraEmpresaORepetido_400()
        {
            Assert.IsTrue(ReglasProyectos.DecidirReparto(Contexto(Apunte(), Proyecto(1)), 555, null, Pedidas((9, 10)), "C", AHORA).Error.Mensaje.Contains("No existe el proyecto 9"));
            Assert.IsTrue(ReglasProyectos.DecidirReparto(Contexto(Apunte(), Proyecto(1, empresa: "3")), 555, null, Pedidas((1, 10)), "C", AHORA).Error.Mensaje.Contains("empresa"));
            Assert.IsTrue(ReglasProyectos.DecidirReparto(Contexto(Apunte(), Proyecto(1)), 555, null, Pedidas((1, 10), (1, 5)), "C", AHORA).Error.Mensaje.Contains("2 veces"));
        }

        [TestMethod]
        public void Reparto_ProyectoCerrado_NoAdmiteNuevas()
        {
            DecisionImputaciones d = ReglasProyectos.DecidirReparto(Contexto(Apunte(), Proyecto(1, ReglasProyectos.ESTADO_CERRADO)), 555, null, Pedidas((1, 10)), "C", AHORA);
            Assert.AreEqual("PROYECTO_CERRADO", d.Error.Codigo);
            Assert.AreEqual(TipoErrorProyecto.Conflicto, d.Error.Tipo);
        }

        [TestMethod]
        public void Reparto_ProyectoCerrado_NoSeQuitaNiSeCambia_PeroSeQuedaSiVieneIgual()
        {
            FilaApunteContable apunte = Apunte();
            FilaProyecto cerrado = Proyecto(1, ReglasProyectos.ESTADO_CERRADO);
            FilaProyecto abierto = Proyecto(2);
            ContextoImputacionApunte contexto = Contexto(apunte, cerrado, abierto);
            contexto.Actuales.Add(Actual(10, cerrado, apunte, 60));

            Assert.AreEqual("PROYECTO_CERRADO", ReglasProyectos.DecidirReparto(contexto, 555, null, Pedidas(), "C", AHORA).Error.Codigo);
            Assert.AreEqual("PROYECTO_CERRADO", ReglasProyectos.DecidirReparto(contexto, 555, null, Pedidas((1, 50)), "C", AHORA).Error.Codigo);

            DecisionImputaciones d = ReglasProyectos.DecidirReparto(contexto, 555, null, Pedidas((1, 60), (2, 60)), "C", AHORA);
            Assert.IsNull(d.Error);
            Assert.AreEqual(0, d.IdsABorrar.Count);
            Assert.AreEqual(1, d.Insertar.Count);
            Assert.AreEqual(2, d.Insertar[0].ProyectoId);
            Assert.AreEqual(2, d.Resultado.Count);
        }

        [TestMethod]
        public void Reparto_ListaVacia_QuitaLoImputado()
        {
            FilaApunteContable apunte = Apunte();
            FilaProyecto p = Proyecto(1);
            ContextoImputacionApunte contexto = Contexto(apunte, p);
            contexto.Actuales.Add(Actual(10, p, apunte, 60));

            DecisionImputaciones d = ReglasProyectos.DecidirReparto(contexto, 555, null, Pedidas(), "C", AHORA);
            Assert.IsNull(d.Error);
            CollectionAssert.AreEqual(new List<int> { 10 }, d.IdsABorrar);
            Assert.AreEqual(0, d.Insertar.Count);
            Assert.IsFalse(d.SinCambios);
        }

        [TestMethod]
        public void Reparto_Igual_SinCambios_YLaQueNoCambiaConservaQuienYCuando()
        {
            FilaApunteContable apunte = Apunte();
            FilaProyecto p1 = Proyecto(1), p2 = Proyecto(2);
            ContextoImputacionApunte contexto = Contexto(apunte, p1, p2);
            contexto.Actuales.Add(Actual(10, p1, apunte, 60, "libros"));

            Assert.IsTrue(ReglasProyectos.DecidirReparto(contexto, 555, null,
                new List<ImputacionPedida> { new ImputacionPedida { ProyectoId = 1, Importe = 60, Comentario = " libros " } }, "C", AHORA).SinCambios);

            DecisionImputaciones d = ReglasProyectos.DecidirReparto(contexto, 555, null,
                new List<ImputacionPedida> { new ImputacionPedida { ProyectoId = 1, Importe = 60, Comentario = "libros" }, new ImputacionPedida { ProyectoId = 2, Importe = 60 } },
                "Carlos", AHORA);
            Assert.IsFalse(d.SinCambios);
            FilaImputacion mantenida = d.Insertar.Single(i => i.ProyectoId == 1);
            Assert.AreEqual("Ana", mantenida.Usuario);
            Assert.AreEqual(new DateTime(2026, 3, 2), mantenida.Fecha);
            Assert.AreEqual("Carlos", d.Insertar.Single(i => i.ProyectoId == 2).Usuario);
        }

        [TestMethod]
        public void Reparto_FueraDeLasFechasDelProyecto_AvisaPeroImputa()
        {
            FilaProyecto p = Proyecto(1);
            p.FechaDesde = new DateTime(2026, 4, 1);
            DecisionImputaciones d = ReglasProyectos.DecidirReparto(Contexto(Apunte(), p), 555, null, Pedidas((1, 10)), "C", AHORA);
            Assert.IsNull(d.Error);
            Assert.AreEqual(1, d.Avisos.Count);
            StringAssert.Contains(d.Avisos[0], "fuera de las fechas");
        }

        // ------------------------------------------------------------ Cuenta de explotación

        private static FilaImputacion Imputacion(int proyecto, string cuenta, decimal debe, decimal haber, decimal importe, int orden)
        {
            var apunte = new FilaApunteContable { Empresa = "1", NumeroOrden = orden, Cuenta = cuenta, NombreCuenta = "C" + cuenta, Fecha = new DateTime(2026, 3, orden % 28 + 1), Debe = debe, Haber = haber };
            FilaImputacion f = ReglasProyectos.Foto(apunte);
            f.Id = orden;
            f.ProyectoId = proyecto;
            f.Importe = importe;
            return f;
        }

        [TestMethod]
        public void CuentaExplotacion_IngresosMenosGastos_ConAbonosRestados()
        {
            FilaProyecto p = Proyecto(1);
            p.PresupuestoIngresos = 5000;
            p.PresupuestoGastos = 3000;
            var imputaciones = new List<FilaImputacion>
            {
                Imputacion(1, "7400000", 0, 6000, 4000, 1),   // subvención: 4000 de 6000
                Imputacion(1, "6230000", 120, 0, 60, 2),      // libros: mitad
                Imputacion(1, "6230000", 0, 20, 10, 3),       // abono de los libros: mitad, resta
                Imputacion(1, "6290000", 2000, 0, 2000, 4),   // profesor
                Imputacion(1, "7050000", 100, 0, 100, 5)      // rectificativa de venta: resta ingresos
            };

            CuentaExplotacionProyectoDTO c = CalculoCuentaExplotacionProyecto.Calcular(p, imputaciones);

            Assert.AreEqual(3900M, c.TotalIngresos);
            Assert.AreEqual(2050M, c.TotalGastos);
            Assert.AreEqual(1850M, c.Resultado);
            Assert.AreEqual(47.44M, c.Margen);
            Assert.AreEqual(2, c.Gastos.Count);
            Assert.AreEqual(50M, c.Gastos.Single(g => g.Cuenta == "6230000").Importe);
            Assert.AreEqual(2, c.Gastos.Single(g => g.Cuenta == "6230000").NumeroApuntes);
            Assert.AreEqual(-100M, c.Ingresos.Single(i => i.Cuenta == "7050000").Importe);
            Assert.AreEqual(5, c.Detalle.Count);
            DetalleCuentaExplotacionDTO libros = c.Detalle.Single(d => d.NumeroOrdenApunte == 2);
            Assert.AreEqual(50M, libros.Porcentaje);
            Assert.AreEqual("Gasto", libros.Naturaleza);
            Assert.AreEqual(-10M, c.Detalle.Single(d => d.NumeroOrdenApunte == 3).Efecto);
            Assert.AreEqual(-1100M, c.Presupuesto.DesviacionIngresos);
            Assert.AreEqual(-950M, c.Presupuesto.DesviacionGastos);
            Assert.AreEqual(2000M, c.Presupuesto.PresupuestoResultado);
            Assert.AreEqual(-150M, c.Presupuesto.DesviacionResultado);
            Assert.AreEqual(5, c.Proyecto.NumeroImputaciones);
        }

        [TestMethod]
        public void CuentaExplotacion_SinPresupuesto_NoLoSaca()
        {
            Assert.IsNull(CalculoCuentaExplotacionProyecto.Calcular(Proyecto(1), new List<FilaImputacion>()).Presupuesto);
        }

        [TestMethod]
        public void Totales_DelResumenPorCuentaYLado_IgualQueLaCuentaDeExplotacion()
        {
            var dto = new ProyectoDTO();
            CalculoCuentaExplotacionProyecto.PonerTotales(dto, new[]
            {
                new ResumenImputacionesProyecto { ProyectoId = 1, Cuenta = "7400000", Lado = -1, Importe = 4000, Numero = 1 },
                new ResumenImputacionesProyecto { ProyectoId = 1, Cuenta = "6230000", Lado = 1, Importe = 60, Numero = 1 },
                new ResumenImputacionesProyecto { ProyectoId = 1, Cuenta = "6230000", Lado = -1, Importe = 10, Numero = 1 },
                new ResumenImputacionesProyecto { ProyectoId = 1, Cuenta = "7050000", Lado = 1, Importe = 100, Numero = 1 }
            });
            Assert.AreEqual(3900M, dto.TotalIngresos);
            Assert.AreEqual(50M, dto.TotalGastos);
            Assert.AreEqual(3850M, dto.Resultado);
            Assert.AreEqual(4, dto.NumeroImputaciones);
        }

        [TestMethod]
        public void Comparativa_UnaColumnaPorProyecto_EnElOrdenPedido()
        {
            var imputaciones = new List<FilaImputacion>
            {
                Imputacion(1, "6230000", 120, 0, 60, 1),
                Imputacion(2, "6230000", 120, 0, 60, 1),
                Imputacion(2, "6290000", 500, 0, 500, 2),
                Imputacion(1, "7400000", 0, 1000, 1000, 3)
            };
            ComparativaProyectosDTO c = CalculoCuentaExplotacionProyecto.Comparar(new List<FilaProyecto> { Proyecto(2), Proyecto(1) }, imputaciones);

            CollectionAssert.AreEqual(new List<int> { 2, 1 }, c.Proyectos.Select(p => p.Id).ToList());
            CollectionAssert.AreEqual(new List<decimal> { 60, 60 }, c.Gastos.Single(g => g.Cuenta == "6230000").Importes);
            CollectionAssert.AreEqual(new List<decimal> { 500, 0 }, c.Gastos.Single(g => g.Cuenta == "6290000").Importes);
            CollectionAssert.AreEqual(new List<decimal> { 0, 1000 }, c.Ingresos.Single().Importes);
            CollectionAssert.AreEqual(new List<decimal> { -560, 940 }, c.Resultados);
        }
    }
}
