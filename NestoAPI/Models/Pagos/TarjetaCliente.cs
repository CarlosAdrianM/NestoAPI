using System;

namespace NestoAPI.Models.Pagos
{
    /// <summary>
    /// NestoAPI#178: una tarjeta guardada de un cliente. El PAN no está en ningún sitio: lo que
    /// se guarda es el token de Redsys (Ds_Merchant_Identifier), que solo sirve en nuestro
    /// comercio. La tabla TarjetasClientes NO está en el EDMX: se accede con SQL crudo desde
    /// <see cref="Infraestructure.Pagos.TarjetaClienteStore"/>.
    /// </summary>
    public class TarjetaCliente
    {
        public int Id { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string TokenRedsys { get; set; }

        /// <summary>
        /// Ds_Merchant_Cof_Txnid del pago inicial. Algunos adquirentes lo exigen en los cobros
        /// MIT posteriores (DS_MERCHANT_COF_TXNID).
        /// </summary>
        public string CofTxnId { get; set; }

        public string UltimosDigitos { get; set; }

        /// <summary>C = crédito, D = débito (Ds_Card_Type).</summary>
        public string TipoTarjeta { get; set; }

        public string MarcaTarjeta { get; set; }
        public DateTime? FechaCaducidad { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaUltimoUso { get; set; }
        public bool Activa { get; set; }
        public string MotivoDesactivacion { get; set; }
        public DateTime? FechaDesactivacion { get; set; }
        public string UsuarioCreacion { get; set; }
        public int IntentosFallidosConsecutivos { get; set; }

        public bool Caducada => FechaCaducidad.HasValue && FechaCaducidad.Value < DateTime.Today;

        /// <summary>
        /// Se puede cobrar con ella ahora mismo. El límite de fallos consecutivos evita
        /// martillear una tarjeta que ya no funciona (se resetea con cada cobro bueno).
        /// </summary>
        public bool Usable => Activa && !Caducada && IntentosFallidosConsecutivos < 3;

        /// <summary>Cómo se le nombra la tarjeta al cliente. Ver <see cref="Describir"/>.</summary>
        public string Descripcion => Describir(MarcaTarjeta, UltimosDigitos, FechaCaducidad);

        /// <summary>
        /// NestoAPI#178: el nombre de la tarjeta para el cliente con lo que tengamos. Los últimos
        /// dígitos NO están garantizados: Redsys solo los manda si el banco activa el envío de
        /// datos de tarjeta en el terminal, y el nuestro no lo tiene. Sin ellos, la marca y la
        /// caducidad ("Visa que caduca en 12/2027") bastan para que el cliente sepa cuál es.
        /// </summary>
        public static string Describir(string marca, string ultimosDigitos, DateTime? caducidad)
        {
            string nombre = string.IsNullOrWhiteSpace(marca) ? "Tarjeta" : marca.Trim();
            if (!string.IsNullOrWhiteSpace(ultimosDigitos))
            {
                return $"{nombre} acabada en {ultimosDigitos.Trim()}";
            }
            if (caducidad.HasValue)
            {
                return $"{nombre} que caduca en {caducidad.Value:MM/yyyy}";
            }
            return string.IsNullOrWhiteSpace(marca) ? "Tarjeta guardada" : nombre;
        }
    }

    /// <summary>
    /// NestoAPI#178: lo que la app necesita saber del cobro con tarjeta guardada. CobroDirecto =
    /// el servidor cobra sin pasarela (MIT); si no, el cliente confirma en la pasarela con la
    /// tarjeta ya cargada (plan B).
    /// </summary>
    public class CapacidadesTarjetasDTO
    {
        public bool CobroDirecto { get; set; }
    }

    /// <summary>
    /// NestoAPI#178: lo que puede ver un cliente de sus tarjetas guardadas. El token NUNCA sale
    /// por la API: cobrar con él es cosa del servidor.
    /// </summary>
    public class TarjetaClienteDTO
    {
        public int Id { get; set; }

        /// <summary>Puede venir vacío: ver <see cref="TarjetaCliente.Describir"/>.</summary>
        public string UltimosDigitos { get; set; }
        public string MarcaTarjeta { get; set; }
        public DateTime? FechaCaducidad { get; set; }
        public bool Caducada { get; set; }
        public DateTime? FechaUltimoUso { get; set; }

        /// <summary>
        /// El texto con el que enseñar la tarjeta ("Visa acabada en 1234", "Visa que caduca en
        /// 12/2027"). Lo compone el servidor para que los clientes no tengan que saber qué datos
        /// faltan.
        /// </summary>
        public string Descripcion { get; set; }

        public static TarjetaClienteDTO Desde(TarjetaCliente tarjeta)
        {
            return new TarjetaClienteDTO
            {
                Id = tarjeta.Id,
                UltimosDigitos = tarjeta.UltimosDigitos,
                Descripcion = tarjeta.Descripcion,
                MarcaTarjeta = tarjeta.MarcaTarjeta,
                FechaCaducidad = tarjeta.FechaCaducidad,
                Caducada = tarjeta.Caducada,
                FechaUltimoUso = tarjeta.FechaUltimoUso
            };
        }
    }
}
