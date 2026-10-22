using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.CorreosPostCompra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>NestoAPI#532 (modo Activo): bajas, repeticiones y lista de vendedores.</summary>
    [TestClass]
    public class ReglasEnvioRecordatorioReposicionTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 22);

        private static RecordatorioReposicionClienteDTO Cliente(string cliente, string vendedor, params (string Producto, int DiasDesdeCompra)[] productos)
            => new RecordatorioReposicionClienteDTO
            {
                Cliente = cliente,
                Email = cliente + "@cliente.es",
                Vendedor = vendedor,
                Productos = productos.Select(p => new CandidatoReposicionDTO
                {
                    Cliente = cliente, Producto = p.Producto, UltimaCompra = HOY.AddDays(-p.DiasDesdeCompra)
                }).ToList()
            };

        private static ResultadoRecordatorioReposicionDTO Resultado(params RecordatorioReposicionClienteDTO[] clientes)
        {
            var resultado = new ResultadoRecordatorioReposicionDTO { Fecha = HOY };
            resultado.SeAvisarian.AddRange(clientes.Where(c => !c.GrupoControl));
            resultado.GrupoControl.AddRange(clientes.Where(c => c.GrupoControl));
            return resultado;
        }

        private static EnvioPrevioReposicion Previo(string cliente, string producto, int haceDias, string grupo = RepositorioRecordatorioReposicionSql.GRUPO_ENVIADO)
            => new EnvioPrevioReposicion { Cliente = cliente, Producto = producto, Fecha = HOY.AddDays(-haceDias).AddHours(9), Grupo = grupo };

        [TestMethod]
        public void LeerVendedores_VacioOSinFila_Ninguno()
        {
            Assert.AreEqual(0, ReglasEnvioRecordatorioReposicion.LeerVendedores(null).Count);
            Assert.AreEqual(0, ReglasEnvioRecordatorioReposicion.LeerVendedores("  , ; ").Count);
            CollectionAssert.AreEqual(new[] { "NV", "(NULL)", "AGR" },
                ReglasEnvioRecordatorioReposicion.LeerVendedores(" nv,(null) ;AGR, NV").ToArray());
        }

        [TestMethod]
        public void VendedorEnLista_NullEsElClienteSinVendedor()
        {
            List<string> lista = ReglasEnvioRecordatorioReposicion.LeerVendedores("NV,(NULL),AGR,ISR,PCN,CAM,LA,DO");
            Assert.IsTrue(ReglasEnvioRecordatorioReposicion.VendedorEnLista(null, lista));
            Assert.IsTrue(ReglasEnvioRecordatorioReposicion.VendedorEnLista("  ", lista));
            Assert.IsTrue(ReglasEnvioRecordatorioReposicion.VendedorEnLista("la ", lista));
            Assert.IsFalse(ReglasEnvioRecordatorioReposicion.VendedorEnLista("JE", lista));
            Assert.IsFalse(ReglasEnvioRecordatorioReposicion.VendedorEnLista(null, new List<string>()), "Lista vacía: nadie, tampoco los sin vendedor");
        }

        [TestMethod]
        public void Planificar_SeparaPorVendedorYElControlSoloSiEstaEnLaLista()
        {
            RecordatorioReposicionClienteDTO control = Cliente("4", "NV", ("A", 40));
            control.GrupoControl = true;
            RecordatorioReposicionClienteDTO controlFuera = Cliente("5", "JE", ("A", 40));
            controlFuera.GrupoControl = true;

            PlanRecordatorioReposicionDTO plan = ReglasEnvioRecordatorioReposicion.Planificar(
                Resultado(Cliente("1", "NV", ("A", 40)), Cliente("2", "JE", ("A", 40)), Cliente("3", null, ("A", 40)), control, controlFuera),
                "NV,(NULL)", null, null, HOY);

            CollectionAssert.AreEqual(new[] { "1", "3" }, plan.Enviar.Select(c => c.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "2" }, plan.FueraPorVendedor.Select(c => c.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { "4" }, plan.Control.Select(c => c.Cliente).ToArray());
        }

        [TestMethod]
        public void Planificar_Baja_PorClienteOPorCorreo_FueraEntero()
        {
            var bajas = new BajasCorreosPostCompra();
            _ = bajas.Clientes.Add("1");
            _ = bajas.Emails.Add("2@CLIENTE.ES");
            ResultadoRecordatorioReposicionDTO resultado = Resultado(Cliente("1", "NV", ("A", 40), ("B", 40)), Cliente("2", "NV", ("A", 40)),
                Cliente("3", "NV", ("A", 40)));

            PlanRecordatorioReposicionDTO plan = ReglasEnvioRecordatorioReposicion.Planificar(resultado, "NV", bajas, null, HOY);

            CollectionAssert.AreEqual(new[] { "3" }, plan.Enviar.Select(c => c.Cliente).ToArray());
            Assert.AreEqual(3, resultado.Descartes.Count(d => d.Motivo == ReglasEnvioRecordatorioReposicion.MOTIVO_BAJA));
        }

        [TestMethod]
        public void Planificar_UnRecordatorioPorProductoYCompra()
        {
            ResultadoRecordatorioReposicionDTO resultado = Resultado(Cliente("1", "NV", ("A", 40), ("B", 40)), Cliente("2", "NV", ("A", 40)));
            var previos = new List<EnvioPrevioReposicion>
            {
                Previo("1", "A", 20),     // después de su última compra (hace 40): ya se le recordó
                Previo("1", "B", 50),     // antes de la última compra: ha vuelto a comprar, se le puede recordar otra vez
                Previo("2", "A", 21, RepositorioRecordatorioReposicionSql.GRUPO_CONTROL)  // el control también cuenta
            };

            PlanRecordatorioReposicionDTO plan = ReglasEnvioRecordatorioReposicion.Planificar(resultado, "NV", null, previos, HOY);

            RecordatorioReposicionClienteDTO uno = plan.Enviar.Single();
            Assert.AreEqual("1", uno.Cliente);
            CollectionAssert.AreEqual(new[] { "B" }, uno.Productos.Select(p => p.Producto).ToArray());
            Assert.AreEqual(2, resultado.Descartes.Count(d => d.Motivo.StartsWith(ReglasEnvioRecordatorioReposicion.MOTIVO_YA_RECORDADO)));
        }

        [TestMethod]
        public void Planificar_UnCorreoCada14DiasComoMucho()
        {
            ResultadoRecordatorioReposicionDTO resultado = Resultado(Cliente("1", "NV", ("NUEVO", 40)), Cliente("2", "NV", ("NUEVO", 40)));
            var previos = new List<EnvioPrevioReposicion>
            {
                Previo("1", "OTRO", 13),  // hace 13 días: todavía no
                Previo("2", "OTRO", 14)   // hace 14: ya sí
            };

            PlanRecordatorioReposicionDTO plan = ReglasEnvioRecordatorioReposicion.Planificar(resultado, "NV", null, previos, HOY);

            CollectionAssert.AreEqual(new[] { "2" }, plan.Enviar.Select(c => c.Cliente).ToArray());
            Assert.IsTrue(resultado.Descartes.Single().Motivo.StartsWith(ReglasEnvioRecordatorioReposicion.MOTIVO_CORREO_RECIENTE));
        }

        [TestMethod]
        public void Planificar_SinResultado_PlanVacio()
        {
            PlanRecordatorioReposicionDTO plan = ReglasEnvioRecordatorioReposicion.Planificar(null, "NV", null, null, HOY);
            Assert.AreEqual(0, plan.Enviar.Count);
        }
    }
}
