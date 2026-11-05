using NestoAPI.Infraestructure.Clientes.Origenes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#623 (corte 5): los leads de campaña (cliente con origen de un catálogo activo, <c>dbo.ClientesOrigen</c>) sin
    /// rapport desde su origen salen los PRIMEROS en «clientes para contactar» de su vendedor, delante incluso de los del cheque
    /// regalo (#593). Es una regla DESPUÉS del motor y del registro del día, como el impulso de los cheques: no se registran en
    /// SugerenciasContacto (SugerenciaId = 0), porque un lead nuevo casi nunca está en la cartera (sin compras en 24 meses) y
    /// tiene que salir aunque la lista del día ya esté fijada.
    /// <list type="bullet">
    /// <item>Motivo: «Lead de Meta: llamar antes de las HH:mm del dd/MM» (<see cref="ReglasLeadsCampana.PlazoLlamadaLead"/>) o,
    /// pasado el plazo, «Lead de Meta: llamada atrasada (desde el dd/MM HH:mm)»; Prioridad Máxima; Origen «LeadCampana».</item>
    /// <item>Entre ellos, primero el que antes vence.</item>
    /// <item>Desaparece en cuanto tiene un rapport (Estado 0 o 1) desde el día de su origen (<see cref="ReglasLeadsCampana.PendienteDeLlamar"/>).</item>
    /// <item>Si el cliente ya estaba en la lista (cualquier contacto), se quita de su puesto: sale una vez, arriba.</item>
    /// </list>
    /// </summary>
    public static class ImpulsoLeadsCampanaContacto
    {
        public const string ORIGEN = "LeadCampana";

        public static List<SugerenciaContactoDTO> Aplicar(IList<SugerenciaContactoDTO> sugerencias, IEnumerable<LeadCampana> leads,
            DateTime ahora, Func<DateTime, bool> esLaborable)
        {
            List<SugerenciaContactoDTO> lista = (sugerencias ?? new List<SugerenciaContactoDTO>()).ToList();
            List<SugerenciaContactoDTO> arriba = (leads ?? Enumerable.Empty<LeadCampana>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Cliente) && ReglasLeadsCampana.PendienteDeLlamar(l, ahora))
                .GroupBy(l => l.Cliente.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Select(l => new { Lead = l, Plazo = ReglasLeadsCampana.PlazoLlamadaLead(l.FechaOrigen, l.HorasParaLlamar, esLaborable) })
                .OrderBy(x => x.Plazo)
                .ThenBy(x => x.Lead.Cliente.Trim(), StringComparer.Ordinal)
                .Select(x => ADTO(x.Lead, x.Plazo, ahora))
                .ToList();
            if (!arriba.Any())
            {
                return lista;
            }
            var clientes = new HashSet<string>(arriba.Select(s => s.Cliente), StringComparer.OrdinalIgnoreCase);
            List<SugerenciaContactoDTO> resultado = arriba
                .Concat(lista.Where(s => !clientes.Contains(s.Cliente?.Trim() ?? string.Empty)))
                .ToList();
            for (int i = 0; i < resultado.Count; i++)
            {
                resultado[i].Orden = i + 1;
            }
            return resultado;
        }

        internal static SugerenciaContactoDTO ADTO(LeadCampana lead, DateTime plazo, DateTime ahora) => new SugerenciaContactoDTO
        {
            SugerenciaId = 0,
            Cliente = lead.Cliente?.Trim(),
            Contacto = lead.Contacto?.Trim(),
            Nombre = lead.Nombre?.Trim(),
            Direccion = lead.Direccion?.Trim(),
            CodigoPostal = lead.CodigoPostal?.Trim(),
            Telefono = lead.Telefono?.Trim(),
            Poblacion = lead.Poblacion?.Trim(),
            Provincia = lead.Provincia?.Trim(),
            Prioridad = PrioridadesContacto.MAXIMA,
            Motivo = ReglasLeadsCampana.Frase(lead, plazo, ahora),
            Origen = ORIGEN,
            DiasDesdeUltimoPedido = 9999
        };
    }
}
