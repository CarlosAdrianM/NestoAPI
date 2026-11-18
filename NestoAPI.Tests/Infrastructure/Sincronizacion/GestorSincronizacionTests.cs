using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#408: el trigger de las tablas sincronizadas encola una fila POR SENTENCIA SQL, así
    /// que un guardado normal deja filas duplicadas (caso real 26/08/26: asignación de vendedor
    /// con dos filas a 13 ms, que acababa en dos mensajes idénticos y dos correos de asignación
    /// en Odoo). ProcesarTabla publica UNA vez por registro modificado.
    /// </summary>
    [TestClass]
    public class GestorSincronizacionTests
    {
        private static NestoSyncRecord Fila(int id, string modificadoId, string usuario = null)
        {
            return new NestoSyncRecord { Id = id, Tabla = "Clientes", ModificadoId = modificadoId, Usuario = usuario };
        }

        [TestMethod]
        public void AgruparPorModificado_FilasDuplicadas_UnaSolaPorRegistro()
        {
            // El caso real: dos filas del cliente 24605 encoladas a 13 ms.
            var registros = new List<NestoSyncRecord> { Fila(27532, "24605"), Fila(27533, "24605") };

            var agrupados = GestorSincronizacion.AgruparPorModificado(registros);

            Assert.AreEqual(1, agrupados.Count, "Dos filas del mismo registro son UN mensaje, no dos");
        }

        [TestMethod]
        public void AgruparPorModificado_ElRepresentanteEsLaUltimaFila_ConSuUsuario()
        {
            // La última fila lleva el usuario del último cambio, y su Id es el tope hasta el que
            // se marca sincronizado (una fila encolada DESPUÉS de leer queda pendiente).
            var registros = new List<NestoSyncRecord>
            {
                Fila(1, "24605", "NUEVAVISION\\Manuel"),
                Fila(2, "24605", "NUEVAVISION\\Laura")
            };

            var agrupados = GestorSincronizacion.AgruparPorModificado(registros);

            Assert.AreEqual(2, agrupados.Single().Id);
            Assert.AreEqual("NUEVAVISION\\Laura", agrupados.Single().Usuario);
        }

        [TestMethod]
        public void AgruparPorModificado_RegistrosDistintos_SeConservanTodosYEnOrdenDeLlegada()
        {
            var registros = new List<NestoSyncRecord>
            {
                Fila(5, "111"),
                Fila(6, "222"),
                Fila(7, "111"),
                Fila(8, "333")
            };

            var agrupados = GestorSincronizacion.AgruparPorModificado(registros);

            CollectionAssert.AreEqual(new[] { "222", "111", "333" },
                agrupados.Select(r => r.ModificadoId).ToArray(),
                "Un registro por ModificadoId, ordenados por su última fila");
        }

        [TestMethod]
        public void AgruparPorModificado_IgnoraElPaddingDeChar()
        {
            // El trigger inserta [Nº Cliente] char(15): la misma referencia puede llegar con y sin
            // blancos de relleno y sigue siendo UN registro.
            var registros = new List<NestoSyncRecord> { Fila(1, "24605          "), Fila(2, "24605") };

            var agrupados = GestorSincronizacion.AgruparPorModificado(registros);

            Assert.AreEqual(1, agrupados.Count);
        }

        [TestMethod]
        public void AgruparPorModificado_SinFilas_ListaVacia()
        {
            Assert.AreEqual(0, GestorSincronizacion.AgruparPorModificado(new List<NestoSyncRecord>()).Count);
        }

        // 18/11/26: tope por pasada en la sincronización de productos (8.078 encolados, una pasada de 98 minutos).

        [TestMethod]
        public void AgruparPorModificado_PorPrimeraFila_LoMasAntiguoPrimeroConElUltimoDeRepresentante()
        {
            var registros = new List<NestoSyncRecord>
            {
                Fila(5, "111", "primero"),
                Fila(6, "222"),
                Fila(7, "111", "ultimo"),
                Fila(8, "333")
            };

            var agrupados = GestorSincronizacion.AgruparPorModificado(registros, porPrimeraFila: true);

            CollectionAssert.AreEqual(new[] { "111", "222", "333" }, agrupados.Select(r => r.ModificadoId).ToArray(),
                "El 111 lleva esperando desde la fila 5: no pasa al final por haberse vuelto a encolar");
            Assert.AreEqual(7, agrupados[0].Id, "El representante sigue siendo la última fila (marca hasta ella)");
            Assert.AreEqual("ultimo", agrupados[0].Usuario);
        }

        [TestMethod]
        public void SeguirCogiendo_SinTope_SiempreSigue()
        {
            var empezo = new System.DateTime(2026, 11, 18, 1, 0, 0);
            Assert.IsTrue(GestorSincronizacion.SeguirCogiendo(empezo, empezo.AddHours(2), null));
        }

        [TestMethod]
        public void SeguirCogiendo_ConTope_SigueAntesYParaAlLlegar()
        {
            var empezo = new System.DateTime(2026, 11, 18, 1, 0, 0);
            System.TimeSpan tope = SincronizacionJobsService.TIEMPO_MAXIMO_PASADA_PRODUCTOS;

            Assert.IsTrue(GestorSincronizacion.SeguirCogiendo(empezo, empezo, tope));
            Assert.IsTrue(GestorSincronizacion.SeguirCogiendo(empezo, empezo + tope - System.TimeSpan.FromSeconds(1), tope));
            Assert.IsFalse(GestorSincronizacion.SeguirCogiendo(empezo, empezo + tope, tope));
            Assert.IsFalse(GestorSincronizacion.SeguirCogiendo(empezo, empezo.AddMinutes(98), tope));
        }

        [TestMethod]
        public void TiempoMaximoPasadaProductos_PorDebajoDeLaRecurrenciaYDelVigilante()
        {
            System.TimeSpan tope = SincronizacionJobsService.TIEMPO_MAXIMO_PASADA_PRODUCTOS;
            Assert.IsTrue(tope < System.TimeSpan.FromMinutes(5), "La recurrencia es cada 5 minutos");
            Assert.IsTrue(tope < NestoAPI.Infraestructure.VigilanciaHangfire.VigilanteHangfire.MAXIMO_EN_MARCHA_POR_COLA[SincronizacionJobsService.COLA]);
        }
    }
}
