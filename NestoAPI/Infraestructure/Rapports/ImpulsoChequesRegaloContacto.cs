using NestoAPI.Infraestructure.ChequesRegalo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>Un cliente de la cartera con el cheque regalo sin usar.</summary>
    public class ClienteConChequeRegalo
    {
        public ClienteCarteraContacto Cliente { get; set; }
        public string Frase { get; set; }
        /// <summary>Ya se habló con él desde que empezó el impulso o no contestó hace menos de 2 días: no se le sube
        /// (pero el motivo sigue diciendo que tiene el cheque).</summary>
        public bool Excluido { get; set; }
    }

    /// <summary>
    /// NestoAPI#593 (estadística): en los últimos días de una campaña de cheque regalo
    /// (<see cref="ReglasEstadisticaChequesRegalo.InicioImpulso"/>, 10 por defecto), los clientes del vendedor con el
    /// cheque sin usar suben en «clientes para contactar». Es una regla DESPUÉS del motor (el modelo no se toca):
    /// <list type="bullet">
    /// <item>Salen con prioridad Máxima y los primeros de las Máxima, aunque por cadencia o por pedido reciente hoy no les
    /// tocara, con el motivo «Tiene un cheque de 50 € sin usar hasta el 07/11» (y detrás el motivo de siempre, si lo hay).</item>
    /// <item>No suben si ya se habló con ellos (rapport T/V/W, Estado 0) desde que empezó el impulso —una barrida, no una
    /// llamada diaria— o si no contestaron hace menos de <see cref="UmbralesSugerenciasContacto.DIAS_TRAS_INTENTO"/> días.</item>
    /// <item>El cheque se busca en la cartera por cliente y contacto de la factura que lo generó; si ese contacto no está
    /// en la cartera del vendedor, el primer contacto del cliente que sí esté.</item>
    /// </list>
    /// </summary>
    public static class ImpulsoChequesRegaloContacto
    {
        /// <summary>Clave de la cartera (cliente/contacto) → el cliente con su cheque.</summary>
        public static Dictionary<string, ClienteConChequeRegalo> Asignar(IEnumerable<ClienteCarteraContacto> cartera,
            IEnumerable<ChequeRegaloImpulso> impulsos, DateTime hoy)
        {
            var resultado = new Dictionary<string, ClienteConChequeRegalo>(StringComparer.OrdinalIgnoreCase);
            List<ClienteCarteraContacto> lista = (cartera ?? Enumerable.Empty<ClienteCarteraContacto>()).ToList();
            foreach (ChequeRegaloImpulso impulso in impulsos ?? Enumerable.Empty<ChequeRegaloImpulso>())
            {
                ClienteCarteraContacto cliente = lista.FirstOrDefault(c => c.Clave.Equals(ClienteCarteraContacto.ClaveDe(impulso.Cliente, impulso.Contacto), StringComparison.OrdinalIgnoreCase))
                    ?? lista.Where(c => string.Equals(c.Cliente?.Trim(), impulso.Cliente?.Trim(), StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Contacto?.Trim(), StringComparer.Ordinal)
                        .FirstOrDefault();
                if (cliente == null || resultado.ContainsKey(cliente.Clave))
                {
                    continue;
                }
                resultado[cliente.Clave] = new ClienteConChequeRegalo
                {
                    Cliente = cliente,
                    Frase = impulso.Frase,
                    Excluido = Excluido(cliente, impulso, hoy)
                };
            }
            return resultado;
        }

        public static bool Excluido(ClienteCarteraContacto cliente, ChequeRegaloImpulso impulso, DateTime hoy)
        {
            int? diasIntento = MotorSugerenciasContacto.DiasDesde(cliente.UltimoIntento, hoy);
            return (cliente.UltimoContacto.HasValue && cliente.UltimoContacto.Value.Date >= impulso.InicioImpulso.Date)
                || (diasIntento.HasValue && diasIntento.Value < UmbralesSugerenciasContacto.DIAS_TRAS_INTENTO);
        }

        /// <summary>
        /// La lista del motor con los clientes del cheque (no excluidos) delante, en Máxima, por probabilidad y días sin
        /// contacto; luego el resto como venía. Orden = 1, 2, 3…
        /// </summary>
        public static List<SugerenciaContactoDTO> Aplicar(IList<SugerenciaContactoDTO> priorizadas, IDictionary<string, ClienteConChequeRegalo> conCheque, DateTime hoy)
        {
            List<SugerenciaContactoDTO> lista = (priorizadas ?? new List<SugerenciaContactoDTO>()).ToList();
            if (conCheque == null || conCheque.Count == 0)
            {
                return lista;
            }
            List<SugerenciaContactoDTO> subidos = conCheque.Values
                .Where(c => !c.Excluido)
                .Select(c => new { Con = c, DiasContacto = MotorSugerenciasContacto.DiasDesde(c.Cliente.UltimoContacto, hoy) })
                .OrderByDescending(x => x.Con.Cliente.Probabilidad)
                .ThenByDescending(x => x.DiasContacto ?? int.MaxValue)
                .ThenBy(x => x.Con.Cliente.Clave, StringComparer.Ordinal)
                .Select(x =>
                {
                    SugerenciaContactoDTO dto = MotorSugerenciasContacto.ADTO(x.Con.Cliente, hoy);
                    dto.Prioridad = PrioridadesContacto.MAXIMA;
                    dto.TieneChequeRegalo = true;
                    dto.Motivo = Motivo(x.Con, hoy);
                    dto.Origen = OrigenesSugerenciaContacto.CHEQUE_REGALO;
                    return dto;
                })
                .ToList();
            var claves = new HashSet<string>(subidos.Select(s => ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto)), StringComparer.OrdinalIgnoreCase);
            List<SugerenciaContactoDTO> resto = lista.Where(s => !claves.Contains(ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto))).ToList();
            foreach (SugerenciaContactoDTO s in resto)
            {
                if (conCheque.TryGetValue(ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto), out ClienteConChequeRegalo con))
                {
                    // Excluido del impulso pero le tocaba igual: con su prioridad, y diciendo que tiene el cheque.
                    s.TieneChequeRegalo = true;
                    s.Motivo = Motivo(con, hoy);
                }
            }
            List<SugerenciaContactoDTO> resultado = subidos.Concat(resto).ToList();
            for (int i = 0; i < resultado.Count; i++)
            {
                resultado[i].Orden = i + 1;
            }
            return resultado;
        }

        /// <summary>La frase del cheque y, detrás, el motivo de siempre si hoy le tocaba por sí solo.</summary>
        public static string Motivo(ClienteConChequeRegalo con, DateTime hoy)
        {
            string prioridadNormal = MotorSugerenciasContacto.Prioridad(con.Cliente, hoy);
            string normal = prioridadNormal != null ? MotorSugerenciasContacto.Motivo(con.Cliente, prioridadNormal, hoy) : null;
            string texto = string.IsNullOrWhiteSpace(normal) ? con.Frase : $"{con.Frase}. {normal}";
            return texto.Length > MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO
                ? texto.Substring(0, MotorSugerenciasContacto.LONGITUD_MAXIMA_MOTIVO)
                : texto;
        }
    }
}
