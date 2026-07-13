#include "pch.h"
#include <GDeflate.h>
#include <cstdint>

extern "C" {
	_declspec(dllexport) int GDeflate_Decompress(
		const uint8_t* compressedData, size_t compressedSize,
		uint8_t* outputBuffer, size_t outputSize,
		uint32_t numWorkers)
	{
		bool ok = GDeflate::Decompress(outputBuffer, outputSize, compressedData, compressedSize, numWorkers);
		return ok ? 0 : 1;
	}

}