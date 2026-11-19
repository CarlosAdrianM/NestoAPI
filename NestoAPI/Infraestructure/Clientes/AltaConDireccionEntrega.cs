using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Data.Entity;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// Alta con dirección de entrega distinta (19/11/26, Carlos): un cliente con dirección fiscal y otra de entrega se da de alta de
    /// una vez (antes, dos pasadas por «Crear cliente», la segunda como contacto). Reglas puras: quién puede traerla, qué le falta y
    /// cómo es el contacto de entrega. El contacto de entrega es un alta de contacto de las de siempre (EsContacto, el mismo
    /// PrepararClienteCrear): hereda del principal lo comercial y las personas de contacto, y solo cambia la dirección.
    /// </summary>
    public static class AltaConDireccionEntrega
    {
        public const string PREFIJO_MOTIVO = "Dirección de entrega: ";

        public const string MOTIVO_SOLO_CLIENTE_NUEVO = "La dirección de entrega distinta solo se puede indicar al dar de alta un cliente nuevo. " +
            "Para añadir una dirección a un cliente que ya existe, dé de alta un contacto.";
        public const string MOTIVO_SIN_DIRECCION_FISCAL = "Para indicar una dirección de entrega distinta, el cliente tiene que tener también su dirección fiscal.";
        public const string MOTIVO_SIN_DIRECCION = PREFIJO_MOTIVO + "falta la dirección (elíjala en el buscador de Google).";
        public const string MOTIVO_SIN_CODIGO_POSTAL = PREFIJO_MOTIVO + "falta el código postal.";

        /// <summary>Null si el alta puede llevar la dirección de entrega que trae (o no trae ninguna); si no, el motivo (400), de usted.</summary>
        public static string MotivoNoAdmitida(ClienteCrear alta)
        {
            DireccionEntregaCrear entrega = alta?.DireccionEntrega;
            if (entrega == null)
            {
                return null;
            }
            if (alta.EsContacto)
            {
                return MOTIVO_SOLO_CLIENTE_NUEVO;
            }
            if (string.IsNullOrWhiteSpace(alta.Direccion))
            {
                return MOTIVO_SIN_DIRECCION_FISCAL;
            }
            if (string.IsNullOrWhiteSpace(entrega.Direccion))
            {
                return MOTIVO_SIN_DIRECCION;
            }
            if (string.IsNullOrWhiteSpace(entrega.CodigoPostal))
            {
                return MOTIVO_SIN_CODIGO_POSTAL;
            }
            return null;
        }

        /// <summary>El motivo de una regla de dirección (Google, número propio, «¿Qué es?»), dicho de la de entrega.</summary>
        public static string DeLaEntrega(string motivo) => string.IsNullOrWhiteSpace(motivo) ? motivo : PREFIJO_MOTIVO + motivo;

        /// <summary>
        /// El alta del contacto de entrega: un alta de contacto (EsContacto) del mismo cliente con lo comercial del principal (NIF, nombre,
        /// país fiscal, estado y tipo de centro, forma y plazos de pago, IBAN, vendedores, días de servir, usuario y aplicación) y la dirección
        /// de <see cref="ClienteCrear.DireccionEntrega"/>. Las personas de contacto son las mismas (Carlos: «son comunes»): el alta las crea
        /// igual en las dos fichas, como deja la copia del principal de NestoAPI#438. No lleva los Comentarios generales (son de la ficha
        /// fiscal), ni origen (va por cliente), ni otra dirección de entrega. Null si el alta no trae dirección de entrega.
        /// </summary>
        public static ClienteCrear ContactoDeEntrega(ClienteCrear principal)
        {
            DireccionEntregaCrear e = principal?.DireccionEntrega;
            if (e == null)
            {
                return null;
            }
            return new ClienteCrear
            {
                Empresa = principal.Empresa,
                Cliente = principal.Cliente,
                EsContacto = true,
                // Lo comercial, del principal
                Nif = principal.Nif,
                Nombre = principal.Nombre,
                Pais = principal.Pais,
                Estado = principal.Estado,
                Estetica = principal.Estetica,
                Peluqueria = principal.Peluqueria,
                Alumna = principal.Alumna,
                Publico = principal.Publico,
                FormaPago = principal.FormaPago,
                PlazosPago = principal.PlazosPago,
                Iban = principal.Iban,
                VendedorEstetica = principal.VendedorEstetica,
                VendedorPeluqueria = principal.VendedorPeluqueria,
                DiasEnServir = principal.DiasEnServir,
                Usuario = principal.Usuario,
                AplicacionAlta = principal.AplicacionAlta,
                PersonasContacto = principal.PersonasContacto,
                // La dirección, la de entrega
                Direccion = e.Direccion,
                DireccionVerificada = e.DireccionVerificada,
                Numero = e.Numero,
                NumeroConfirmadoSinGoogle = e.NumeroConfirmadoSinGoogle,
                TipoLugar = e.TipoLugar,
                Piso = e.Piso,
                Puerta = e.Puerta,
                NumeroLocal = e.NumeroLocal,
                Rotulo = e.Rotulo,
                NumeroNave = e.NumeroNave,
                OtroTexto = e.OtroTexto,
                CodigoPostal = e.CodigoPostal,
                Poblacion = e.Poblacion,
                Provincia = e.Provincia,
                Ruta = e.Ruta,
                Telefono = e.Telefono,
                ComentariosRuta = e.ComentariosRuta,
                ComentariosPicking = e.ComentariosPicking
            };
        }

        /// <summary>
        /// El número del contacto de entrega: el siguiente al del principal. El cliente es nuevo (su número se acaba de dar libre), así que
        /// no hay otro; no se pregunta a la BD porque con el principal aún sin guardar daría el mismo número.
        /// </summary>
        public static string SiguienteContacto(string contactoPrincipal)
            => int.TryParse(contactoPrincipal?.Trim(), out int numero) ? (numero + 1).ToString() : "1";

        /// <summary>
        /// Si las dos direcciones comparten un código postal que el alta tiene que CREAR, cada preparación ha añadido su fila al contexto
        /// y la segunda chocaría con la primera (PK_CódigosPostales): la de entrega se quita y usa la del principal.
        /// </summary>
        internal static void CompartirCodigoPostalNuevo(NVEntities db, Cliente principal, Cliente entrega)
        {
            CodigoPostal delPrincipal = principal?.CódigosPostales;
            CodigoPostal deLaEntrega = entrega?.CódigosPostales;
            if (delPrincipal == null || deLaEntrega == null || ReferenceEquals(delPrincipal, deLaEntrega)
                || !string.Equals(delPrincipal.Número?.Trim(), deLaEntrega.Número?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            db.Entry(deLaEntrega).State = EntityState.Detached;
            entrega.CódigosPostales = delPrincipal;
        }
    }

    /// <summary>La transacción del alta con dirección de entrega (sustituible en los tests, que no tienen BD).</summary>
    public interface ITransaccionAltaCliente : IDisposable
    {
        void Commit();
    }

    internal sealed class TransaccionAltaClienteEf : ITransaccionAltaCliente
    {
        private readonly DbContextTransaction transaccion;

        public TransaccionAltaClienteEf(DbContextTransaction transaccion)
        {
            this.transaccion = transaccion;
        }

        public void Commit() => transaccion.Commit();

        public void Dispose() => transaccion.Dispose();
    }
}
