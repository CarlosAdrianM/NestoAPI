using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models;
using NestoAPI.Models.Inventarios;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// 25/11/26 (botón «Borrar» de la lista «Completo» de Inventario Tienda): GET api/Inventarios trae SePuedeBorrar en cada fila con la
    /// regla del DELETE api/Inventarios/Filas/{n}.
    /// </summary>
    [TestClass]
    public class InventariosSePuedeBorrarTests
    {
        private const string PALOMA = @"NUEVAVISION\Paloma";
        private static readonly DateTime HOY = new DateTime(2026, 11, 25, 10, 0, 0);

        private static Inventario Fila(int numero, DateTime fecha, string usuario = PALOMA, byte estado = 1, int? traspaso = null)
            => new Inventario { NºOrden = numero, Empresa = "1", Almacén = "ALC", Fecha = fecha, Número = "17404", Usuario = usuario, Estado = estado, NºTraspaso = traspaso };

        private static ComprobacionesInventarioDTO SobraLa(int numero) => new ComprobacionesInventarioDTO
        {
            Bloqueantes = new List<ComprobacionInventarioDTO>
            {
                new ComprobacionInventarioDTO
                {
                    Codigo = CodigosComprobacionInventario.SIN_UBICACION,
                    Filas = new List<FilaComprobacionInventarioDTO> { new FilaComprobacionInventarioDTO { NºOrden = numero, Fecha = HOY.Date, Producto = "17404" } }
                }
            }
        };

        [TestMethod]
        public void Regla_ComoElDelete()
        {
            // Sin permiso de contar en el almacén, nunca
            Assert.IsFalse(ReglaBorrarFilaInventario.SePuedeBorrarFila(false, true, true, HOY.AddDays(-1), PALOMA, PALOMA, HOY, () => true));
            // Su propia fila de ayer, sin el permiso de borrar
            Assert.IsTrue(ReglaBorrarFilaInventario.SePuedeBorrarFila(true, false, true, HOY.AddDays(-1), PALOMA, "paloma", HOY, () => false));
            // La de ayer de otro, solo con el permiso
            Assert.IsFalse(ReglaBorrarFilaInventario.SePuedeBorrarFila(true, false, true, HOY.AddDays(-1), @"NUEVAVISION\Laura", PALOMA, HOY, null));
            Assert.IsTrue(ReglaBorrarFilaInventario.SePuedeBorrarFila(true, true, true, HOY.AddDays(-1), @"NUEVAVISION\Laura", PALOMA, HOY, null));
            // De hoy, solo si sobra
            Assert.IsFalse(ReglaBorrarFilaInventario.SePuedeBorrarFila(true, true, true, HOY.Date, PALOMA, PALOMA, HOY, () => false));
            Assert.IsTrue(ReglaBorrarFilaInventario.SePuedeBorrarFila(true, true, true, HOY.Date, PALOMA, PALOMA, HOY, () => true));
            // De un día posterior o contabilizada, nunca
            Assert.IsFalse(ReglaBorrarFilaInventario.SePuedeBorrarFila(true, true, true, HOY.AddDays(1), PALOMA, PALOMA, HOY, () => true));
            Assert.IsFalse(ReglaBorrarFilaInventario.SePuedeBorrarFila(true, true, false, HOY.AddDays(-1), PALOMA, PALOMA, HOY, () => true));
        }

        [TestMethod]
        public async Task Marcar_LasComprobacionesDeHoySeLeenUnaVezYSoloSiHacenFalta()
        {
            var filas = new List<Inventario> { Fila(1, HOY.Date), Fila(2, HOY.Date), Fila(3, HOY.Date.AddDays(-2)), Fila(4, HOY.Date, traspaso: 81000) };
            int lecturas = 0;

            await InventariosController.MarcarSePuedeBorrar(filas, true, true, PALOMA, HOY, () => { lecturas++; return Task.FromResult(SobraLa(2)); });

            Assert.AreEqual(1, lecturas);
            Assert.IsFalse(filas[0].SePuedeBorrar, "De hoy y no sobra");
            Assert.IsTrue(filas[1].SePuedeBorrar, "De hoy y sobra");
            Assert.IsTrue(filas[2].SePuedeBorrar, "De un día anterior");
            Assert.IsFalse(filas[3].SePuedeBorrar, "Con traspaso no está pendiente");
        }

        [TestMethod]
        public async Task Marcar_SinPermisoDeBorrar_SoloSusFilasDeDiasAnteriores_SinLeerComprobaciones()
        {
            var filas = new List<Inventario> { Fila(1, HOY.Date), Fila(2, HOY.Date.AddDays(-1)), Fila(3, HOY.Date.AddDays(-1), @"NUEVAVISION\Laura") };
            int lecturas = 0;

            await InventariosController.MarcarSePuedeBorrar(filas, true, false, PALOMA, HOY, () => { lecturas++; return Task.FromResult(SobraLa(1)); });

            Assert.AreEqual(0, lecturas);
            Assert.IsFalse(filas[0].SePuedeBorrar);
            Assert.IsTrue(filas[1].SePuedeBorrar);
            Assert.IsFalse(filas[2].SePuedeBorrar);
        }

        [TestMethod]
        public void SePuedeBorrar_SaleEnElJsonYSeIgnoraAlRecibirlo()
        {
            var fila = Fila(1, HOY.Date);
            fila.SePuedeBorrar = true;
            fila.PendienteDeDiaAnterior = true;

            string json = JsonConvert.SerializeObject(fila, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });
            StringAssert.Contains(json, "\"SePuedeBorrar\":true");
            StringAssert.Contains(json, "\"PendienteDeDiaAnterior\":true");

            Inventario recibida = JsonConvert.DeserializeObject<Inventario>(json);
            Assert.IsFalse(recibida.SePuedeBorrar);
            Assert.IsFalse(recibida.PendienteDeDiaAnterior);
        }
    }
}
