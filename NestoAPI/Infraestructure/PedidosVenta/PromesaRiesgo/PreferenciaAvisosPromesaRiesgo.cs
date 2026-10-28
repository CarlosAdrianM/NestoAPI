using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Carlos (28/10/26): «a Manuel y a mí nos va a saturar tanto mensaje de pedidos en riesgo»: cada usuario elige qué recibe
    /// (parámetro de usuario <see cref="Parametros.Claves.AvisosPedidosEnRiesgo"/>): Todo (campana y correo resumen, lo de
    /// siempre), SoloCorreo, SoloCampana o Nada. La ventana de pedidos en riesgo no cambia: se sigue viendo todo lo que toca.
    /// Sin fila (ni la del usuario ni «(defecto)»), Todo.
    /// </summary>
    public static class PreferenciaAvisosPromesaRiesgo
    {
        public const string TODO = "Todo";
        public const string SOLO_CORREO = "SoloCorreo";
        public const string SOLO_CAMPANA = "SoloCampana";
        public const string NADA = "Nada";
        public const string USUARIO_DEFECTO = "(defecto)";

        public static readonly IReadOnlyList<string> OPCIONES = new[] { TODO, SOLO_CORREO, SOLO_CAMPANA, NADA };

        /// <summary>El valor bien escrito; null o desconocido = Todo.</summary>
        public static string Normalizar(string valor)
        {
            string limpio = valor?.Trim();
            return OPCIONES.FirstOrDefault(o => string.Equals(o, limpio, StringComparison.OrdinalIgnoreCase)) ?? TODO;
        }

        public static bool EsValido(string valor) => OPCIONES.Any(o => string.Equals(o, valor?.Trim(), StringComparison.OrdinalIgnoreCase));

        public static bool Campana(string valor)
        {
            string v = Normalizar(valor);
            return v == TODO || v == SOLO_CAMPANA;
        }

        public static bool Correo(string valor)
        {
            string v = Normalizar(valor);
            return v == TODO || v == SOLO_CORREO;
        }

        public static string Texto(string valor)
        {
            switch (Normalizar(valor))
            {
                case SOLO_CORREO: return "Solo el correo resumen";
                case SOLO_CAMPANA: return "Solo la campana";
                case NADA: return "Nada (solo la ventana)";
                default: return "Campana y correo resumen";
            }
        }

        /// <summary>Lo guardado para el usuario (sin dominio); si no tiene fila, la de «(defecto)»; si tampoco, Todo.</summary>
        public static string Leer(NVEntities db, string usuario)
        {
            string sinDominio = ComercialesPedido.SinDominio(usuario);
            if (db == null || string.IsNullOrWhiteSpace(sinDominio))
            {
                return TODO;
            }
            List<ParametroUsuario> filas = db.ParametrosUsuario
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Clave == Parametros.Claves.AvisosPedidosEnRiesgo
                    && (p.Usuario == sinDominio || p.Usuario == USUARIO_DEFECTO))
                .ToList();
            ParametroUsuario fila = filas.FirstOrDefault(f => string.Equals(f.Usuario?.Trim(), sinDominio, StringComparison.OrdinalIgnoreCase))
                ?? filas.FirstOrDefault(f => string.Equals(f.Usuario?.Trim(), USUARIO_DEFECTO, StringComparison.OrdinalIgnoreCase));
            return Normalizar(fila?.Valor);
        }

        /// <summary>Guarda (o crea) la fila del usuario. Devuelve el valor normalizado.</summary>
        public static string Guardar(NVEntities db, string usuario, string valor)
        {
            string sinDominio = ComercialesPedido.SinDominio(usuario);
            if (db == null || string.IsNullOrWhiteSpace(sinDominio))
            {
                throw new ArgumentException("Falta el usuario.", nameof(usuario));
            }
            string normalizado = Normalizar(valor);
            ParametroUsuario fila = db.ParametrosUsuario.FirstOrDefault(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                && p.Clave == Parametros.Claves.AvisosPedidosEnRiesgo && p.Usuario == sinDominio);
            if (fila == null)
            {
                fila = new ParametroUsuario
                {
                    Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO,
                    Clave = Parametros.Claves.AvisosPedidosEnRiesgo,
                    Usuario = sinDominio
                };
                db.ParametrosUsuario.Add(fila);
            }
            fila.Valor = normalizado;
            fila.Usuario2 = sinDominio;
            fila.Fecha_Modificación = DateTime.Now;
            db.SaveChanges();
            return normalizado;
        }
    }

    /// <summary>GET/PUT api/PedidosVenta/PromesaEnRiesgo/Avisos: lo elegido y las opciones, para el desplegable de Nesto.</summary>
    public class AvisosPromesaRiesgoDTO
    {
        public string Valor { get; set; }
        public string Texto { get; set; }
        public List<OpcionAvisosPromesaRiesgoDTO> Opciones { get; set; } = new List<OpcionAvisosPromesaRiesgoDTO>();

        public static AvisosPromesaRiesgoDTO De(string valor)
        {
            string v = PreferenciaAvisosPromesaRiesgo.Normalizar(valor);
            return new AvisosPromesaRiesgoDTO
            {
                Valor = v,
                Texto = PreferenciaAvisosPromesaRiesgo.Texto(v),
                Opciones = PreferenciaAvisosPromesaRiesgo.OPCIONES
                    .Select(o => new OpcionAvisosPromesaRiesgoDTO { Valor = o, Texto = PreferenciaAvisosPromesaRiesgo.Texto(o) }).ToList()
            };
        }
    }

    public class OpcionAvisosPromesaRiesgoDTO
    {
        public string Valor { get; set; }
        public string Texto { get; set; }
    }
}
