Please visit page: 

# ParticleSystem

## Introduction

Within the context of this project, our primary objective was centered around the creation of a particle system within the Unity environment, coupled with the integration of a visually engaging visualization. Unlike the marching cubes algorithm, which focuses on generating 3D models from data using techniques like Perlin noise, the particle system is designed to simulate and render individual particles in a dynamic and cohesive manner. The emphasis was on crafting a system that allows for the dynamic and captivating representation of particle-based effects within the Unity framework.

--- 

## Running Instructions

To run this project, first ensure that you have Unity3D development environment installed on your computer. After that, download the project file and unpack it to the desired location. Then, start Unity3D, choose the "Open Project" option, and navigate to the main project directory.

The first scene, named "Particles" represents the space where a particle system with several exemplary instances is located. Open this scene to gain insight into various visual effects created by particles. In this scene, you can experiment with particle system settings and customize them according to your preferences.

---

## Particle System Settings

- NOTE 1: Using Start and End stuff means interpolating between them during lifetime
- NOTE 2: There are more settings which are analogue to ones below.

### Setting Fields

- **Seed:** Seed for random generation.
- **Simulation Space:** Decides whether CPS moves with root or not.
- **Render Type:** Type of rendering. (billboard, point or mesh)
- **Draw GUI:** Whether to draw helper GUI.

### Render Stuff

- **Billboard Texture:** Texture used for billboard particles.
- **Particle Mesh:** Mesh used for non-billboard particles.

### Properties

- **Start Position Generator:** Initial position stuff (Shape in which particles generate).
- **Start Lifetime Generator:** Lifetime.
- **Start Velocity Generator:** Initial velocity.
- **Uniform Scale Generator:** Initial size stuff.
- **Use End Scale:** Whether to interpolate scale.
  - **Start Scale Generator:** Initial size stuff.
  - **End Scale Generator:** Generator for the end scale.
- **Start Rotation Generator:** Initial rotation stuff.
  - **Rotation Over Time Generator:** Generator for rotation over time.
- **Uniform Colour Generator:** Initial color stuff.
- **Use End Colour:** Whether to interpolate colors.
  - **Start Colour Generator:** Initial color stuff.
  - **End Colour Generator:** Generator for the end color.

### Simulation Fields

- **Maximum Particle Count:** Max count of concurrent particles.
- **Current Particle Count:** Used for tracking the current number of concurrent particles.
- **Emission Rate:** Amount of particles emitted per second.
  - **Emission Amount:** Tracks the emission amount for the current frame.
- **Gravity:** Gravity intensity.
- **Drag:** Drag intensity - slows down particles.
- **Use Force Fields:** Turns ForceFields ON or OFF.
  - **Force Fields:** Environmental ForceField stuff (walls, attractors, repulsors).
- **Use Vector Field:** Turns VectorFields ON or OFF. (example: using gradient of 3D function to decide movement -> e.g. winds)
  - **Vector Field Frequency:** Environmental VectorField frequency.
  - **Vector Field Intensity:** Environmental VectorField intensity.

---

## Implementation

The project relies on several dependencies and components to achieve its functionality. Below are the key elements involved in the implementation:

### Scripts

1. **CPS.cs:** This C# script serves as a bridge between the CPU and GPU. It is responsible for communicating with the GPU, sending parameters, and visualizing the particles. The script facilitates the coordination of operations between the CPU and GPU components.

2. **CPSBillboardShader.shader && CPSMeshShader.shader:** These scripts are designed to render the mesh properly using vertex, gemoetry and pixel shaders. It ensures the correct visualization of the generated mesh on the GPU side.

3. **CPSSimulator.compute:** The bulk of operations occurs in this compute shader. Here, particles are emitted, simulated, killed trough updating GPU buffers. It serves as the computational core where the intricate calculations take place.
   
All three files must effectively communicate as they share the same data for the entire system to function seamlessly.

---
---
---

# CubeMarcher

## Introduction

Within the scope of this project, the focus was on developing the "marching cubes" algorithm in the Unity environment, along with implementing the visualization of the results. The marching cubes algorithm is used to generate 3D models from given data, in this case, Perlin noise as a density function. The goal was to create a system that enables precise visualization of complex 3D structures from provided data.

--- 

## Running Instructions

To run this project, first ensure that you have Unity3D development environment installed on your computer. After that, download the project file and unpack it to the desired location. Then, start Unity3D, choose the "Open Project" option, and navigate to the main project directory.

The second scene, named "MarchingCubes," contains an exemplary implementation of the marching cubes algorithm. Open this scene to explore the generation of 3D models using this algorithm. You can observe the results and adjust parameters to obtain different shapes and structures.

---

## Settings

### Basic

- **Material:** Defines the material for visualizing the generated mesh.
- **Body Size:** Sets the uniform size of the body, with default values set to 1.
- **Density Threshold:** Determines the density threshold that distinguishes the interior and exterior of the body (values from 0 to 1).
- **Mesh Resolution:** Sets the resolution of the generated mesh.

### Perlin Noise Settings (Generating Functions)

- **Octaves:** Defines the number of octaves in the Perlin noise.
- **Lacunarity:** Sets the lacunarity parameter of the Perlin noise.
- **Frequency:** Determines the frequency of the Perlin noise.
- **Offset:** Adjusts the offset parameter of the Perlin noise.

### Animation Settings

- **Frequency Speed:** Controls the speed of frequency function changes.
- **Offset Speed:** Influences the speed of offset function changes.
- **Lacunarity Speed:** Regulates the speed of lacunarity parameter changes.

---

## Implementation

The project relies on several dependencies and components to achieve its functionality. Below are the key elements involved in the implementation:

### Dependencies

1. **FastNoiseLite:** A robust HLSL noise library utilized for generating procedural noise in the project.

2. **MarchingTable:** This component is employed to store all possible configurations of a mesh contained inside a cube chunk. It plays a crucial role in the mesh generation process.

### Scripts

1. **StableCubeMarcher.cs:** This C# script serves as a bridge between the CPU and GPU. It is responsible for communicating with the GPU, sending parameters, and visualizing the generated mesh. The script facilitates the coordination of operations between the CPU and GPU components.

2. **StableMarchShader.shader:** This script is designed to render the mesh properly using vertex and pixel shaders. It ensures the correct visualization of the generated mesh on the GPU side.

3. **StableCubeMarcher.compute:** The bulk of operations occurs in this compute shader. Here, noise is sampled, the mesh is constructed, and buffers are updated. It serves as the computational core where the intricate calculations take place.
   
All three files must effectively communicate as they share the same data for the entire system to function seamlessly.
