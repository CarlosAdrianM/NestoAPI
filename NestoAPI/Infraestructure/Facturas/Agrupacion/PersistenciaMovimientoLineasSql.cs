using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Core.Objects;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#729: el movimiento de líneas en la BD, por SQL. Hasta el 29/10/26 el motor cambiaba <c>linea.Número</c> en la
    /// entidad seguida: EF6 no lo admite («The property 'Número' is part of the object's key information and cannot be
    /// modified») y, como el fallo se quedaba en el contexto compartido, tumbaba después la facturación de rutas entera
    /// (Alfredo, 09:18: 7 pedidos sin albarán ni factura por el grupo de P.O. de Chanel).
    /// </summary>
    internal class PersistenciaMovimientoLineasSql : IPersistenciaMovimientoLineas
    {
        /// <summary>Órdenes por UPDATE (SQL Server admite 2100 parámetros por comando).</summary>
        internal const int TAMANO_LOTE = 500;

        internal const string SQL_MARCAR_AGRUPADA =
            "UPDATE CabPedidoVta SET Agrupada = 1 WHERE Empresa = @empresa AND Número = @destino;";

        private readonly NVEntities db;

        public PersistenciaMovimientoLineasSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public void Mover(string empresa, int destino, IReadOnlyCollection<int> ordenes, IEnumerable<CabPedidoVta> pedidosDelGrupo)
        {
            // Lo que la estrategia haya cambiado en memoria (el contacto del destino) se guarda antes de soltar nada: aquí el
            // contexto aún está sano (ninguna clave tocada).
            _ = db.SaveChanges();
            Soltar(pedidosDelGrupo);

            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                foreach (List<int> lote in Lotes(ordenes))
                {
                    _ = db.Database.ExecuteSqlCommand(SqlMoverLineas(lote.Count), Parametros(empresa, destino, lote));
                }
                _ = db.Database.ExecuteSqlCommand(SQL_MARCAR_AGRUPADA, Parametros(empresa, destino, new List<int>()));
                transaccion.Commit();
            }
        }

        /// <summary>
        /// Las mismas tres actualizaciones que prdAgruparAlbaranesVta: la línea cambia de pedido (solo si sigue en albarán, como la
        /// vista) y PedidosEspeciales y Ubicaciones, que apuntan a la línea por su Nº Orden, repuntan al destino.
        /// </summary>
        internal static string SqlMoverLineas(int numeroOrdenes)
        {
            if (numeroOrdenes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(numeroOrdenes));
            }
            string lista = string.Join(", ", Enumerable.Range(0, numeroOrdenes).Select(i => "@o" + i));
            return
                $"UPDATE LinPedidoVta SET Número = @destino WHERE Empresa = @empresa AND Estado = {Constantes.EstadosLineaVenta.ALBARAN} AND [Nº Orden] IN ({lista});\r\n" +
                $"UPDATE PedidosEspeciales SET NºPedidoVta = @destino WHERE Empresa = @empresa AND NºOrdenVta IN ({lista});\r\n" +
                $"UPDATE Ubicaciones SET PedidoVta = @destino WHERE Empresa = @empresa AND NºOrdenVta IN ({lista});";
        }

        internal static IEnumerable<List<int>> Lotes(IReadOnlyCollection<int> ordenes)
        {
            List<int> distintas = (ordenes ?? new List<int>()).Distinct().ToList();
            for (int i = 0; i < distintas.Count; i += TAMANO_LOTE)
            {
                yield return distintas.Skip(i).Take(TAMANO_LOTE).ToList();
            }
        }

        private static object[] Parametros(string empresa, int destino, List<int> lote)
        {
            var parametros = new List<object>
            {
                new SqlParameter("@empresa", empresa),
                new SqlParameter("@destino", destino)
            };
            parametros.AddRange(lote.Select((orden, i) => new SqlParameter("@o" + i, orden)));
            return parametros.ToArray();
        }

        /// <summary>
        /// Suelta del contexto los pedidos del grupo y sus líneas. Con ObjectContext.Detach (no con DbContext.Entry, que llama a
        /// DetectChanges). Primero las líneas y luego la cabecera, para que ninguna entidad seguida siga apuntando a una suelta:
        /// si quedara alguna, el siguiente DetectChanges la volvería a enganchar como nueva.
        /// </summary>
        private void Soltar(IEnumerable<CabPedidoVta> pedidos)
        {
            ObjectContext contexto = ((IObjectContextAdapter)db).ObjectContext;
            foreach (CabPedidoVta pedido in (pedidos ?? Enumerable.Empty<CabPedidoVta>()).Where(p => p != null).Distinct().ToList())
            {
                foreach (LinPedidoVta linea in (pedido.LinPedidoVtas ?? Enumerable.Empty<LinPedidoVta>()).ToList())
                {
                    Soltar(contexto, linea);
                }
                Soltar(contexto, pedido);
            }
        }

        private static void Soltar(ObjectContext contexto, object entidad)
        {
            if (contexto.ObjectStateManager.TryGetObjectStateEntry(entidad, out ObjectStateEntry entrada)
                && entrada.State != EntityState.Detached)
            {
                contexto.Detach(entidad);
            }
        }
    }
}
