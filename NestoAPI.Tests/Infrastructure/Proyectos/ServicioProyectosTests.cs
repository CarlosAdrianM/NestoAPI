using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Proyectos;
using NestoAPI.Models.Proyectos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Proyectos
{
    /// <summary>NestoAPI#901: el servicio de proyectos con un repositorio en memoria.</summary>
    [TestClass]
    public class ServicioProyectosTests
    {
        private RepositorioProyectosFalso repositorio;
        private ServicioProyectos servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioProyectosFalso();
            servicio = new ServicioProyectos(repositorio, () => new DateTime(2026, 11, 25, 10, 0, 0));
        }

        private static DatosProyecto Datos(string codigo = "impe0210-2026-1") => new DatosProyecto
        {
            Codigo = codigo, Nombre = "IMPE0210 primera", Tipo = "CursoSEPE", FechaDesde = new DateTime(2026, 2, 1), Producto = "IMPE0210"
        };

        [TestMethod]
        public async Task SinTablas_503EnTodo()
        {
            repositorio.HayTablas = false;
            Assert.AreEqual(TipoErrorProyecto.SinTabla, (await servicio.Listar("1", true, null, null)).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.SinTabla, (await servicio.Crear(Datos(), "C")).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.SinTabla, (await servicio.ApuntesPorImputar(new PeticionApuntesPorImputar())).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.SinTabla, (await servicio.RepartirApunte(1, new PeticionImputacionesApunteDTO(), "C")).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.SinTabla, (await servicio.CuentaExplotacion(1)).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.SinTabla, (await servicio.Comparativa("1", null, "1")).Error.Tipo);
            Assert.AreEqual(ServicioProyectos.MENSAJE_SIN_TABLA, (await servicio.Leer(1)).Error.Mensaje);
        }

        [TestMethod]
        public async Task Crear_NormalizaYDejaRastro_YElCodigoRepetidoEs409()
        {
            ResultadoProyectos<ProyectoDTO> creado = await servicio.Crear(Datos(), "Carlos");
            Assert.IsTrue(creado.Ok);
            Assert.AreEqual("IMPE0210-2026-1", creado.Valor.Codigo);
            Assert.AreEqual("Abierto", creado.Valor.Estado);
            Assert.AreEqual(1, repositorio.Rastro.Count);

            ResultadoProyectos<ProyectoDTO> repetido = await servicio.Crear(Datos("IMPE0210-2026-1"), "Carlos");
            Assert.AreEqual(TipoErrorProyecto.Conflicto, repetido.Error.Tipo);
            Assert.AreEqual("CODIGO_REPETIDO", repetido.Error.Codigo);
        }

        [TestMethod]
        public async Task Crear_DatosMalos_400SinTocarNada()
        {
            ResultadoProyectos<ProyectoDTO> r = await servicio.Crear(new DatosProyecto { Codigo = "MAL CODIGO" }, "C");
            Assert.AreEqual(TipoErrorProyecto.Invalido, r.Error.Tipo);
            Assert.AreEqual(0, repositorio.Proyectos.Count);
        }

        [TestMethod]
        public async Task Listar_ConTotales_YSoloAbiertos()
        {
            FilaProyecto abierto = repositorio.Proyecto("IMPE0210-2026-1");
            repositorio.Proyecto("IMPE0210-2025-1", ReglasProyectos.ESTADO_CERRADO);
            repositorio.Imputar(abierto, repositorio.Apunte(1, "6230000", 120, 0), 60);
            repositorio.Imputar(abierto, repositorio.Apunte(2, "7400000", 0, 1000), 1000);

            List<ProyectoDTO> lista = (await servicio.Listar("1", true, null, null)).Valor;
            Assert.AreEqual(1, lista.Count);
            Assert.AreEqual(1000M, lista[0].TotalIngresos);
            Assert.AreEqual(60M, lista[0].TotalGastos);
            Assert.AreEqual(940M, lista[0].Resultado);
            Assert.AreEqual(2, lista[0].NumeroImputaciones);
            Assert.AreEqual(2, (await servicio.Listar("1", false, null, null)).Valor.Count);
            Assert.AreEqual(TipoErrorProyecto.Invalido, (await servicio.Listar("1", true, "Congreso", null)).Error.Tipo);
        }

        [TestMethod]
        public async Task Modificar_Cerrado409_YNoExiste404()
        {
            FilaProyecto cerrado = repositorio.Proyecto("IMPE0210-2025-1", ReglasProyectos.ESTADO_CERRADO);
            Assert.AreEqual("PROYECTO_CERRADO", (await servicio.Modificar(cerrado.Id, Datos("IMPE0210-2025-1"), "C")).Error.Codigo);
            Assert.AreEqual(TipoErrorProyecto.NoExiste, (await servicio.Modificar(99, Datos(), "C")).Error.Tipo);
        }

        [TestMethod]
        public async Task CerrarYReabrir_CambianElEstado()
        {
            FilaProyecto p = repositorio.Proyecto("IMPE0210-2026-1");
            Assert.AreEqual("Cerrado", (await servicio.Cerrar(p.Id, "C")).Valor.Estado);
            Assert.AreEqual("Abierto", (await servicio.Reabrir(p.Id, "C")).Valor.Estado);
            Assert.AreEqual(2, repositorio.Rastro.Count);
        }

        [TestMethod]
        public async Task Borrar_ConImputaciones409_SinEllasSeBorra()
        {
            FilaProyecto con = repositorio.Proyecto("CON");
            FilaProyecto sin = repositorio.Proyecto("SIN");
            repositorio.Imputar(con, repositorio.Apunte(1, "6230000", 120, 0), 60);

            Assert.AreEqual("PROYECTO_CON_IMPUTACIONES", (await servicio.Borrar(con.Id, "C")).Error.Codigo);
            Assert.IsTrue((await servicio.Borrar(sin.Id, "C")).Ok);
            Assert.IsFalse(repositorio.Proyectos.Any(p => p.Id == sin.Id));
        }

        [TestMethod]
        public void ValidarApuntes_PorDefectoDelUnoDeEneroAHoyYCuentas6y7()
        {
            ErrorProyecto error = ServicioProyectos.ValidarApuntes(new PeticionApuntesPorImputar(), new DateTime(2026, 11, 25), out FiltroApuntesProyecto filtro);
            Assert.IsNull(error);
            Assert.AreEqual(new DateTime(2026, 1, 1), filtro.Desde);
            Assert.AreEqual(new DateTime(2026, 11, 25), filtro.Hasta);
            Assert.AreEqual(0, filtro.Cuentas.Count);
            Assert.AreEqual(ServicioProyectos.TOPE_APUNTES, filtro.Tope);
            Assert.IsTrue(filtro.SoloPendientes);
        }

        [DataTestMethod]
        [DataRow("430")]
        [DataRow("62A")]
        [DataRow("6,4")]
        public void ValidarApuntes_CuentaQueNoEsDe6Ni7_400(string cuenta)
        {
            Assert.IsNotNull(ServicioProyectos.ValidarApuntes(new PeticionApuntesPorImputar { Cuenta = cuenta }, new DateTime(2026, 11, 25), out _));
        }

        [TestMethod]
        public void ValidarApuntes_VariasCuentasYTopeMaximo()
        {
            Assert.IsNull(ServicioProyectos.ValidarApuntes(new PeticionApuntesPorImputar { Cuenta = "623, 705", Tope = 9000 }, new DateTime(2026, 11, 25), out FiltroApuntesProyecto filtro));
            CollectionAssert.AreEqual(new List<string> { "623", "705" }, filtro.Cuentas.ToList());
            Assert.AreEqual(500, filtro.Tope);
        }

        [TestMethod]
        public void ValidarApuntes_FechasAlReves400()
        {
            Assert.IsNotNull(ServicioProyectos.ValidarApuntes(new PeticionApuntesPorImputar { Desde = new DateTime(2026, 5, 1), Hasta = new DateTime(2026, 4, 1) },
                new DateTime(2026, 11, 25), out _));
        }

        [TestMethod]
        public async Task ApuntesPorImputar_ConLoImputadoYLoPendiente()
        {
            FilaProyecto p = repositorio.Proyecto("IMPE0210-2026-1");
            FilaApunteContable libros = repositorio.Apunte(1, "6230000", 120, 0);
            repositorio.Apunte(2, "6290000", 500, 0);
            FilaApunteContable entero = repositorio.Apunte(3, "6290000", 80, 0);
            repositorio.Imputar(p, libros, 60);
            repositorio.Imputar(p, entero, 80);

            ApuntesPorImputarDTO r = (await servicio.ApuntesPorImputar(new PeticionApuntesPorImputar())).Valor;
            Assert.AreEqual(2, r.Apuntes.Count);
            ApunteProyectoDTO a = r.Apuntes.Single(x => x.NumeroOrden == 1);
            Assert.AreEqual(120M, a.ImporteApunte);
            Assert.AreEqual(60M, a.Imputado);
            Assert.AreEqual(60M, a.Pendiente);
            Assert.AreEqual(50M, a.PorcentajeImputado);
            Assert.AreEqual("Gasto", a.Naturaleza);
            Assert.AreEqual("IMPE0210-2026-1", a.Imputaciones.Single().CodigoProyecto);
            Assert.AreEqual(3, (await servicio.ApuntesPorImputar(new PeticionApuntesPorImputar { SoloPendientes = false })).Valor.Apuntes.Count);
        }

        [TestMethod]
        public async Task Repartir_EntreDosConvocatorias_YQuitarlo()
        {
            FilaProyecto p1 = repositorio.Proyecto("IMPE0210-2026-1");
            FilaProyecto p2 = repositorio.Proyecto("IMPE0210-2026-2");
            repositorio.Apunte(555, "6230000", 120, 0);

            ResultadoProyectos<ImputacionesApunteDTO> r = await servicio.RepartirApunte(555, new PeticionImputacionesApunteDTO
            {
                Empresa = "1",
                ImporteApunte = 120,
                Imputaciones = new List<ImputacionPedidaDTO>
                {
                    new ImputacionPedidaDTO { ProyectoId = p1.Id, Importe = 80 },
                    new ImputacionPedidaDTO { ProyectoId = p2.Id, Importe = 40 }
                }
            }, "Carlos");

            Assert.IsTrue(r.Ok, r.Error?.Mensaje);
            Assert.IsTrue(r.Valor.Cambiado);
            Assert.AreEqual(2, r.Valor.Imputaciones.Count);
            Assert.AreEqual(66.67M, r.Valor.Imputaciones.Single(i => i.ProyectoId == p1.Id).Porcentaje);
            Assert.AreEqual(0M, r.Valor.Apunte.Pendiente);
            Assert.AreEqual(1, repositorio.Rastro.Count(x => x.Item1 == "ProyectoImputacion"));

            ResultadoProyectos<ImputacionesApunteDTO> otraVez = await servicio.RepartirApunte(555, new PeticionImputacionesApunteDTO
            {
                Imputaciones = new List<ImputacionPedidaDTO>
                {
                    new ImputacionPedidaDTO { ProyectoId = p2.Id, Importe = 40 },
                    new ImputacionPedidaDTO { ProyectoId = p1.Id, Importe = 80 }
                }
            }, "Carlos");
            Assert.IsFalse(otraVez.Valor.Cambiado);
            Assert.AreEqual(1, repositorio.Rastro.Count(x => x.Item1 == "ProyectoImputacion"));

            ResultadoProyectos<ImputacionesApunteDTO> quitado = await servicio.RepartirApunte(555, new PeticionImputacionesApunteDTO { Imputaciones = new List<ImputacionPedidaDTO>() }, "Carlos");
            Assert.IsTrue(quitado.Valor.Cambiado);
            Assert.AreEqual(0, repositorio.Imputaciones.Count);
        }

        [TestMethod]
        public async Task Repartir_ApunteOcupado_409()
        {
            repositorio.ApunteOcupado = true;
            ResultadoProyectos<ImputacionesApunteDTO> r = await servicio.RepartirApunte(555, new PeticionImputacionesApunteDTO(), "C");
            Assert.AreEqual("APUNTE_OCUPADO", r.Error.Codigo);
        }

        [TestMethod]
        public async Task ImputacionesDeApunte_NoExiste404()
        {
            Assert.AreEqual(TipoErrorProyecto.NoExiste, (await servicio.ImputacionesDeApunte("1", 12345)).Error.Tipo);
        }

        [TestMethod]
        public async Task Comparativa_PorProductoOPorIds()
        {
            FilaProyecto p1 = repositorio.Proyecto("IMPE0210-2026-1", producto: "IMPE0210", desde: new DateTime(2026, 1, 1));
            FilaProyecto p2 = repositorio.Proyecto("IMPE0210-2026-2", producto: "IMPE0210", desde: new DateTime(2026, 6, 1));
            repositorio.Proyecto("FERIA-2026");
            repositorio.Imputar(p1, repositorio.Apunte(1, "6230000", 120, 0), 60);

            ComparativaProyectosDTO porProducto = (await servicio.Comparativa(null, "IMPE0210", "1")).Valor;
            CollectionAssert.AreEqual(new List<int> { p1.Id, p2.Id }, porProducto.Proyectos.Select(p => p.Id).ToList());
            CollectionAssert.AreEqual(new List<decimal> { 60, 0 }, porProducto.TotalesGastos);

            Assert.AreEqual(TipoErrorProyecto.NoExiste, (await servicio.Comparativa($"{p1.Id},99", null, "1")).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.Invalido, (await servicio.Comparativa("1,a", null, "1")).Error.Tipo);
            Assert.AreEqual(TipoErrorProyecto.Invalido, (await servicio.Comparativa(null, null, "1")).Error.Tipo);
        }

        [TestMethod]
        public async Task CuentaExplotacion_NoExiste404_YExisteConLoImputado()
        {
            Assert.AreEqual(TipoErrorProyecto.NoExiste, (await servicio.CuentaExplotacion(77)).Error.Tipo);
            FilaProyecto p = repositorio.Proyecto("IMPE0210-2026-1");
            repositorio.Imputar(p, repositorio.Apunte(1, "7400000", 0, 1000), 1000);
            CuentaExplotacionProyectoDTO c = (await servicio.CuentaExplotacion(p.Id)).Valor;
            Assert.AreEqual(1000M, c.Resultado);
            Assert.AreEqual(1, c.Detalle.Count);
        }

        [TestMethod]
        public void Excel_TieneResumenYDetalle()
        {
            FilaProyecto p = new FilaProyecto { Id = 1, Empresa = "1", Codigo = "IMPE0210-2026-1", Nombre = "X", Tipo = "CursoSEPE", Estado = "Abierto", FechaDesde = new DateTime(2026, 1, 1), PresupuestoGastos = 100 };
            FilaApunteContable apunte = new FilaApunteContable { Empresa = "1", NumeroOrden = 1, Cuenta = "6230000", Fecha = new DateTime(2026, 3, 1), Debe = 120 };
            FilaImputacion imputacion = ReglasProyectos.Foto(apunte);
            imputacion.ProyectoId = 1;
            imputacion.Importe = 60;
            CuentaExplotacionProyectoDTO cuenta = CalculoCuentaExplotacionProyecto.Calcular(p, new[] { imputacion });

            byte[] bytes = new GeneradorExcelCuentaExplotacionProyecto().GenerarBytes(cuenta);
            Assert.IsTrue(bytes.Length > 1000);
            Assert.AreEqual((byte)'P', bytes[0]);
            Assert.AreEqual((byte)'K', bytes[1]);
            Assert.AreEqual("CuentaExplotacion_IMPE0210-2026-1.xlsx", GeneradorExcelCuentaExplotacionProyecto.NombreFichero(cuenta));
        }
    }
}
