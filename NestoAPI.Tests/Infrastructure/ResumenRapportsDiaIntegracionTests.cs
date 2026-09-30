using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.IO;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// El resumen diario de rapports contra la base de datos de verdad: que las consultas del
    /// controlador se traducen a SQL (la proyección a una clase propia con el cliente de cada
    /// rapport) y que con datos reales la cabecera sale entera. Solo lectura; no manda ningún correo
    /// ni llama a la IA.
    ///
    /// <para>Solo corre con NESTO_TEST_BD. Si además NESTO_TEST_SALIDA apunta a una carpeta, deja
    /// allí la cabecera en HTML y el texto que leería la IA, para verlos antes de publicar.</para>
    /// </summary>
    [TestClass]
    public class ResumenRapportsDiaIntegracionTests
    {
        private const string EMPRESA = "1";
        // Un martes normal: siete vendedores en activo en el equipo de ASH y una sin ningún rapport
        private static readonly DateTime DIA = new DateTime(2026, 9, 29);

        private static string[] EquipoDe(NVEntities db, string jefe)
        {
            return db.EquiposVentas
                .Where(e => e.Empresa == EMPRESA && e.Superior == jefe)
                .Select(e => e.Vendedor).ToList()
                .Select(v => v.Trim()).Distinct().ToArray();
        }

        private static void Guardar(string nombre, string contenido)
        {
            string carpeta = Environment.GetEnvironmentVariable("NESTO_TEST_SALIDA");
            if (!string.IsNullOrWhiteSpace(carpeta) && Directory.Exists(carpeta))
            {
                File.WriteAllText(Path.Combine(carpeta, nombre), contenido);
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public void Integracion_ElResumenDeUnEquipo_LeeLosRapportsConSuClienteYMontaLaCabecera()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                string[] equipo = EquipoDe(db, "ASH");
                Assert.IsTrue(equipo.Length > 0, "El equipo de ASH tiene que existir en EquiposVenta");

                var datos = SeguimientosClientesController.LeerDatosResumenDia(db, EMPRESA, DIA, equipo, false);

                Assert.IsTrue(datos.Rapports.Count > 0);
                Assert.IsTrue(datos.Rapports.All(r => equipo.Contains(r.Vendedor.Trim())), "Solo rapports del equipo");
                Assert.IsTrue(datos.Rapports.Any(r => !string.IsNullOrWhiteSpace(r.NombreCliente)), "Trae el nombre del cliente");
                Assert.IsTrue(datos.Rapports.Any(r => !string.IsNullOrWhiteSpace(r.VendedorCliente)), "Trae el vendedor del cliente");
                Assert.IsTrue(datos.Esperados.All(v => datos.Fichas[v.Trim()].Estado >= 0), "No se espera rapport de quien está de baja");

                string cabecera = ResumenRapportsDia.CabeceraHtml(DIA, datos.Rapports, datos.Fichas, datos.Esperados);
                string paraLaIA = ResumenRapportsDia.TextoParaIA(DIA, datos.Rapports, datos.Fichas);

                StringAssert.Contains(cabecera, "Actividad del día 29/09/2026");
                foreach (string vendedor in datos.Rapports.Select(r => r.Vendedor.Trim()).Distinct())
                {
                    StringAssert.Contains(cabecera, "(" + vendedor + ")", "Todo el que ha metido rapports sale en la tabla");
                    StringAssert.Contains(paraLaIA, "(" + vendedor + ")", "Y en lo que lee la IA, con su nombre");
                }

                Guardar("rapports_equipo_cabecera.html", cabecera);
                Guardar("rapports_equipo_para_la_ia.txt", paraLaIA);
            }
        }

        [TestMethod]
        [TestCategory("Integracion")]
        public void Integracion_ElResumenDelResto_EsperaRapportDeLosQueVienenMetiendolos()
        {
            using (NVEntities db = BaseDeDatosDeIntegracion.AbrirNVEntities())
            {
                string[] equipo = EquipoDe(db, "ASH");

                var datos = SeguimientosClientesController.LeerDatosResumenDia(db, EMPRESA, DIA, equipo, true);

                Assert.IsTrue(datos.Rapports.Count > 0);
                // OJO: en el «resto» entran también los rapports SIN vendedor (Entity Framework traduce
                // el «no está en la lista» incluyendo los nulos). El resumen tiene que aguantarlos.
                Assert.IsFalse(datos.Rapports.Any(r => r.Vendedor != null && equipo.Contains(r.Vendedor.Trim())), "Ninguno del equipo");
                Assert.IsTrue(datos.Esperados.Count > 0);
                Assert.IsFalse(datos.Esperados.Any(v => equipo.Contains(v.Trim())));

                string cabecera = ResumenRapportsDia.CabeceraHtml(DIA, datos.Rapports, datos.Fichas, datos.Esperados);
                string paraLaIA = ResumenRapportsDia.TextoParaIA(DIA, datos.Rapports, datos.Fichas);
                StringAssert.Contains(cabecera, "Actividad del día 29/09/2026");
                if (datos.Rapports.Any(r => string.IsNullOrWhiteSpace(r.Vendedor)))
                {
                    StringAssert.Contains(cabecera, "(sin vendedor)");
                }

                Guardar("rapports_resto_cabecera.html", cabecera);
                Guardar("rapports_resto_para_la_ia.txt", paraLaIA);
            }
        }
    }
}
