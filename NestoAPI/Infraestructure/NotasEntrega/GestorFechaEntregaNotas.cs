using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>Una nota de entrega de un pedido que todavía no tiene fecha de entrega (NestoAPI#582).</summary>
    public class NotaEntregaSinFechaDTO
    {
        public int Numero { get; set; }
        public int Albaran { get; set; }
    }

    /// <summary>
    /// NestoAPI#582: la nota de entrega automática nace sin fecha (<see cref="CreadorNotaEntregaPendiente.FECHA_ENTREGA_SIN_DETERMINAR"/>)
    /// para no colarse en el picking del día. Nesto, al hacer el albarán desde el detalle, pregunta cuándo se entrega:
    /// aquí se buscan las notas sin fecha de ese pedido y se les pone la que diga el usuario.
    /// </summary>
    public class GestorFechaEntregaNotas
    {
        private readonly NVEntities db;

        public GestorFechaEntregaNotas(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        /// <summary>Las notas que nacieron de ese pedido y alguna de cuyas líneas sigue sin fecha.</summary>
        public async Task<List<NotaEntregaSinFechaDTO>> NotasSinFecha(string empresa, int pedido)
        {
            DateTime sinFecha = CreadorNotaEntregaPendiente.FECHA_ENTREGA_SIN_DETERMINAR;
            List<CabPedidoVta> notas = await db.CabPedidoVtas
                .Where(c => c.Empresa == empresa && c.PedidoOrigen == pedido && c.NotaEntrega)
                .ToListAsync().ConfigureAwait(false);
            List<int> numeros = notas.Select(n => n.Número).ToList();
            List<int> conLineasSinFecha = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && numeros.Contains(l.Número) && l.Fecha_Entrega == sinFecha)
                .Select(l => l.Número)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            return notas
                .Where(n => conLineasSinFecha.Contains(n.Número))
                .OrderBy(n => n.Número)
                .Select(n => new NotaEntregaSinFechaDTO { Numero = n.Número, Albaran = n.AlbaranOrigen ?? 0 })
                .ToList();
        }

        /// <summary>Pone la fecha a las líneas pendientes de la nota y quita el aviso. Devuelve el motivo si no se puede.</summary>
        public async Task<string> PonerFecha(string empresa, int nota, DateTime fechaEntrega, string usuario)
        {
            CabPedidoVta cabecera = await db.CabPedidoVtas
                .SingleOrDefaultAsync(c => c.Empresa == empresa && c.Número == nota).ConfigureAwait(false);
            if (cabecera == null || !cabecera.NotaEntrega)
            {
                return $"El pedido {nota} no es una nota de entrega.";
            }
            if (fechaEntrega.Date < Hoy())
            {
                return $"La fecha de entrega {fechaEntrega:dd/MM/yyyy} ya ha pasado.";
            }
            List<LinPedidoVta> lineas = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Número == nota && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .ToListAsync().ConfigureAwait(false);
            if (lineas.Any(l => (l.Picking ?? 0) != 0))
            {
                return $"La nota {nota} ya tiene picking: la fecha de entrega se cambia desde el pedido.";
            }

            foreach (LinPedidoVta linea in lineas)
            {
                linea.Fecha_Entrega = fechaEntrega.Date;
                linea.Usuario = usuario;
            }
            cabecera.Comentarios = QuitarAviso(cabecera.Comentarios);
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            return null;
        }

        internal static string QuitarAviso(string comentarios)
        {
            if (string.IsNullOrEmpty(comentarios))
            {
                return comentarios;
            }
            return comentarios
                .Replace("\r\n" + CreadorNotaEntregaPendiente.AVISO_SIN_FECHA, string.Empty)
                .Replace(CreadorNotaEntregaPendiente.AVISO_SIN_FECHA, string.Empty)
                .TrimEnd();
        }
    }
}
