using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#603: la tabla SugerenciasContacto. Se registra UNA vez por vendedor y día (la primera consulta inserta; las
    /// siguientes reutilizan las filas y solo añaden si piden más) y se marca Atendida cuando hay un rapport del cliente
    /// ese mismo día. Con esto se sabe quién usa la lista y si las sugerencias venden más que las llamadas libres.
    /// </summary>
    public class RegistroSugerenciasContacto
    {
        public const int LONGITUD_USUARIO = 30;

        private readonly NVEntities db;

        public RegistroSugerenciasContacto(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<List<SugerenciaContacto>> LeerDelDia(string vendedor, DateTime dia)
        {
            string vendedorLimpio = vendedor?.Trim();
            DateTime desde = dia.Date;
            DateTime hasta = desde.AddDays(1);
            List<SugerenciaContacto> filas = await db.SugerenciasContacto
                .Where(s => s.Vendedor == vendedorLimpio && s.Fecha >= desde && s.Fecha < hasta)
                .ToListAsync().ConfigureAwait(false);
            return filas.OrderBy(s => s.Orden).ToList();
        }

        /// <summary>
        /// Las sugerencias del día de las que ya hay rapport (de cualquier vendedor o usuario: es el cliente el que se
        /// atendió) pasan a Atendida. Cubre los rapports que no entran por el POST de SeguimientosClientes.
        /// Devuelve true si ha cambiado alguna (falta guardar).
        /// </summary>
        public async Task<bool> ConciliarAtendidas(IList<SugerenciaContacto> delDia, DateTime dia)
        {
            List<SugerenciaContacto> pendientes = delDia.Where(s => !s.Atendida).ToList();
            if (!pendientes.Any())
            {
                return false;
            }
            DateTime desde = dia.Date;
            DateTime hasta = desde.AddDays(1);
            List<string> clientes = pendientes.Select(s => s.Cliente.Trim()).Distinct().ToList();
            var rapports = await db.SeguimientosClientes
                .Where(r => r.Fecha >= desde && r.Fecha < hasta && clientes.Contains(r.Número))
                .Select(r => new { r.NºOrden, r.Número, r.Contacto, r.Fecha })
                .ToListAsync().ConfigureAwait(false);

            bool cambios = false;
            foreach (SugerenciaContacto sugerencia in pendientes)
            {
                var rapport = rapports
                    .Where(r => r.Número?.Trim() == sugerencia.Cliente.Trim() && r.Contacto?.Trim() == sugerencia.Contacto.Trim())
                    .OrderBy(r => r.Fecha)
                    .FirstOrDefault();
                if (rapport != null)
                {
                    Marcar(sugerencia, rapport.NºOrden, rapport.Fecha);
                    cambios = true;
                }
            }
            return cambios;
        }

        /// <summary>Añade las sugerencias nuevas (aún sin guardar) con Orden a continuación de las que ya hubiera.</summary>
        public List<SugerenciaContacto> Anadir(IEnumerable<SugerenciaContactoDTO> nuevas, int ordenInicial, string vendedor, string usuario, DateTime ahora)
        {
            var filas = new List<SugerenciaContacto>();
            int orden = ordenInicial;
            foreach (SugerenciaContactoDTO dto in nuevas)
            {
                var fila = new SugerenciaContacto
                {
                    Fecha = ahora,
                    Vendedor = vendedor?.Trim(),
                    Usuario = Recortar(string.IsNullOrWhiteSpace(usuario) ? "(desconocido)" : usuario.Trim(), LONGITUD_USUARIO),
                    Cliente = dto.Cliente,
                    Contacto = dto.Contacto,
                    Prioridad = dto.Prioridad,
                    Orden = ++orden,
                    Probabilidad = dto.Probabilidad,
                    Motivo = Recortar(dto.Motivo ?? string.Empty, MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO),
                    Atendida = false
                };
                _ = db.SugerenciasContacto.Add(fila);
                filas.Add(fila);
            }
            return filas;
        }

        public Task<int> Guardar() => db.SaveChangesAsync();

        /// <summary>
        /// Al crear un rapport: si el cliente tenía sugerencia ese día sin atender, se marca Atendida con el rapport.
        /// Guarda. Devuelve cuántas ha marcado.
        /// </summary>
        public async Task<int> MarcarAtendidaPorRapport(string cliente, string contacto, DateTime fechaRapport, int rapportId)
        {
            if (string.IsNullOrWhiteSpace(cliente))
            {
                return 0;
            }
            string clienteLimpio = cliente.Trim();
            string contactoLimpio = contacto?.Trim() ?? string.Empty;
            DateTime desde = fechaRapport.Date;
            DateTime hasta = desde.AddDays(1);
            List<SugerenciaContacto> filas = await db.SugerenciasContacto
                .Where(s => s.Cliente == clienteLimpio && s.Contacto == contactoLimpio && s.Fecha >= desde && s.Fecha < hasta && !s.Atendida)
                .ToListAsync().ConfigureAwait(false);
            if (!filas.Any())
            {
                return 0;
            }
            foreach (SugerenciaContacto fila in filas)
            {
                Marcar(fila, rapportId, fechaRapport);
            }
            _ = await db.SaveChangesAsync().ConfigureAwait(false);
            return filas.Count;
        }

        private static void Marcar(SugerenciaContacto fila, int rapportId, DateTime fechaRapport)
        {
            fila.Atendida = true;
            fila.RapportId = rapportId;
            fila.FechaAtendida = fechaRapport;
        }

        internal static string Recortar(string texto, int longitud) => texto.Length > longitud ? texto.Substring(0, longitud) : texto;
    }
}
