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
    }
}
