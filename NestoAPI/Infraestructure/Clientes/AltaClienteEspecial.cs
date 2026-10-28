using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// Nesto#555 (sugerencia 770 de Paloma + comentario 411 de Laura): en el asistente de alta, además de «¿Tiene Estética?» y
    /// «¿Tiene Peluquería?», se puede dar de alta una ALUMNA (cursos y eventos) o un cliente de PÚBLICO FINAL (profesional de
    /// otro sector que pide factura). Quien decide el estado y el vendedor es la API, no el cliente que llama:
    /// Alumna → Estado 10 y vendedor NV; Público → Estado 8 y vendedor NV. Sin estética ni peluquería (no es cartera de nadie).
    /// </summary>
    public static class AltaClienteEspecial
    {
        public const string MOTIVO_EXCLUYENTES = "Un cliente no puede ser alumna y público final a la vez: marque solo una de las dos opciones.";

        /// <summary>True si el alta es de alumna o de público final (y entonces ya ha puesto estado, vendedores y marcas).</summary>
        public static bool Aplicar(ClienteCrear clienteCrear)
        {
            if (clienteCrear == null || (!clienteCrear.Alumna && !clienteCrear.Publico))
            {
                return false;
            }
            if (clienteCrear.Alumna && clienteCrear.Publico)
            {
                throw new ArgumentException(MOTIVO_EXCLUYENTES);
            }
            clienteCrear.Estado = clienteCrear.Alumna ? Constantes.Clientes.Estados.ALUMNA : Constantes.Clientes.Estados.PUBLICO_FINAL;
            clienteCrear.Estetica = false;
            clienteCrear.Peluqueria = false;
            clienteCrear.VendedorEstetica = Constantes.Vendedores.VENDEDOR_GENERAL;
            clienteCrear.VendedorPeluqueria = Constantes.Vendedores.VENDEDOR_GENERAL;
            return true;
        }
    }
}
